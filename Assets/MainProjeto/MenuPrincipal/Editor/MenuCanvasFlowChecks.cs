using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MainProjeto.MenuPrincipal.Scripts;
using MainProjeto.Telemetria.Participants;
using MainProjeto.Telemetria.Sessions;
using MainProjeto.Telemetria.Supabase;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MainProjeto.MenuPrincipal.Editor
{
    // Executar em uma instancia batch separada. Usa copia temporaria da cena e dados ficticios.
    [InitializeOnLoad]
    public static class MenuCanvasFlowChecks
    {
        private const string RunningKey = "CircuitosVR.MenuCanvasChecks.Running";
        private const string ResultKey = "CircuitosVR.MenuCanvasChecks.Result";
        private const string TestScene = "Assets/__MenuCanvasFlowChecks.unity";
        private static int checks;

        static MenuCanvasFlowChecks()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(RunningKey, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += RunInPlayMode;
                if (state == PlayModeStateChange.EnteredEditMode) Finish();
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Execute este teste pelo comando batch documentado.");
            try
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
                // Nunca executar login real, ler o cofre ou consultar o banco durante os testes.
                foreach (var auto in UnityEngine.Object.FindObjectsByType<AutomaticOperatorLogin>(FindObjectsInactive.Include, FindObjectsSortMode.None)) auto.enabled = false;
                foreach (var refresh in UnityEngine.Object.FindObjectsByType<ParticipantAutoRefresh>(FindObjectsInactive.Include, FindObjectsSortMode.None)) refresh.enabled = false;
                if (!EditorSceneManager.SaveScene(scene, TestScene)) throw new InvalidOperationException("Nao foi possivel criar a copia de teste.");
                SessionState.SetBool(RunningKey, true);
                SessionState.SetInt(ResultKey, 1);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception)
            {
                Debug.LogError("MENU_CANVAS_CHECKS: FAIL ao preparar a cena de teste.");
                EditorApplication.Exit(1);
            }
        }

        private static async void RunInPlayMode()
        {
            try
            {
                await Task.Delay(250);
                var menu = UnityEngine.Object.FindFirstObjectByType<MenuIntegrationController>();
                var ui = UnityEngine.Object.FindFirstObjectByType<ParticipantSelectionUI>();
                var main = Field<GameObject>(menu, "mainPanel");
                var list = Field<GameObject>(menu, "participantPanel");
                var start = Field<Button>(menu, "startButton");
                var open = Field<Button>(menu, "openParticipantsButton");
                var refresh = Field<Button>(ui, "refreshButton");
                var back = Field<Button>(ui, "backButton");
                var exit = main.GetComponentsInChildren<Button>(true).Single(button => button.name == "ButtonSair");
                var context = ApplicationSessionContext.Instance;
                Check(main.activeInHierarchy && !list.activeInHierarchy, "entrada no menu principal");
                Check(!start.interactable, "iniciar bloqueado sem participante");
                Check(main.GetComponentsInChildren<Button>().Length == 3, "tres botoes no menu");
                open.onClick.Invoke();
                Check(list.activeInHierarchy && !main.activeInHierarchy, "selecionar abre a lista");
                Check(!start.gameObject.activeInHierarchy && !exit.gameObject.activeInHierarchy, "iniciar e sair ocultos na lista");
                Check(refresh.gameObject.activeInHierarchy && back.gameObject.activeInHierarchy, "atualizar e voltar visiveis");
                back.onClick.Invoke();
                Check(main.activeInHierarchy && !list.activeInHierarchy, "voltar funciona antes da conexao");

                var client = new RosterFixture();
                ui.Initialize(new ParticipantRepository(client));
                var auth = SupabaseAuthSession.FromAccessToken(FixtureJwt(), DateTimeOffset.UtcNow);
                Authenticate(menu, auth);
                Check(main.activeInHierarchy && !list.activeInHierarchy, "login automatico nao troca painel");
                Check(ui.HasValidList && !start.interactable, "lista carregada exige escolha explicita");
                open.onClick.Invoke();
                var row = list.GetComponentsInChildren<ParticipantListItemUI>().Single(item => item.ParticipantCode == RosterFixture.First);
                Field<Button>(row, "selectButton").onClick.Invoke();
                Check(context.Participants.Selected.ParticipantCode == RosterFixture.First, "selecao usa UUID");
                Check(Field<TMP_Text>(row, "actionText").text == "Selecionado", "linha escolhida tem identificacao visual");
                string sessionId = context.Session.SessionId;
                back.onClick.Invoke();
                Check(main.activeInHierarchy && start.interactable, "voltar habilita iniciar no menu");
                Check(context.Session.SessionId == sessionId, "voltar preserva a sessao preparada");
                Check(Field<TMP_Text>(menu, "selectedSummary").text.Contains("Pessoa teste"), "menu mostra participante escolhido");
                Authenticate(menu, auth);
                Check(main.activeInHierarchy && !list.activeInHierarchy && start.interactable, "renovacao preserva menu e escolha");

                open.onClick.Invoke();
                client.Pending = new TaskCompletionSource<string>();
                refresh.onClick.Invoke();
                Check(ui.IsLoading && !start.interactable, "atualizacao manual bloqueia inicio durante consulta");
                back.onClick.Invoke();
                Check(main.activeInHierarchy && !start.interactable, "voltar durante carregamento nao libera inicio");
                var pending = client.Pending;
                client.Pending = null;
                pending.SetResult(client.Rows);
                await WaitForRoster(ui);
                Check(main.activeInHierarchy && start.interactable, "consulta termina em segundo plano sem trocar painel");
                Check(context.Session.SessionId == sessionId, "atualizacao nao duplica sessao");

                open.onClick.Invoke();
                client.Fail = true;
                refresh.onClick.Invoke();
                Check(!ui.HasValidList && !start.interactable, "falha de rede bloqueia inicio");
                Check(context.Participants.Selected.ParticipantCode == RosterFixture.First, "falha nao apaga identidade");
                back.onClick.Invoke();
                Check(main.activeInHierarchy && !start.interactable, "voltar apos erro mantem inicio bloqueado");
                open.onClick.Invoke();
                client.Fail = false;
                refresh.onClick.Invoke();
                Check(ui.HasValidList && start.interactable, "nova consulta valida recupera inicio");
                client.Rows = "[{\"participant_code\":\"" + RosterFixture.Second + "\",\"display_name\":\"Pessoa teste\"}]";
                refresh.onClick.Invoke();
                Check(context.Participants.Selected == null && !start.interactable, "remocao de UUID escolhido invalida escolha mesmo com nome igual");
                client.Rows = "[]";
                refresh.onClick.Invoke();
                Check(ui.HasValidList && !start.interactable, "lista vazia nao libera inicio");
                Check(Field<TMP_Text>(ui, "statusText").text.Contains("Nenhum participante"), "lista vazia tem mensagem");
                back.onClick.Invoke();
                Check(main.activeInHierarchy && !start.interactable, "voltar sem escolher mantem bloqueio");
                start.onClick.Invoke(); // Invocacao programatica tambem precisa passar pela validacao.
                Check(context.Session.State == LocalSessionState.None, "chamada de iniciar sem escolha nao cria sessao");
                back.onClick.Invoke();
                CheckMainLayout(menu, main);

                SessionState.SetInt(ResultKey, 0);
                Debug.Log("MENU_CANVAS_CHECKS: PASS " + checks + " verificacoes em Play Mode, sem rede.");
            }
            catch (Exception ex)
            {
                Debug.LogError("MENU_CANVAS_CHECKS: FAIL " + ex.GetType().Name + ": " + ex.Message);
            }
            finally { EditorApplication.isPlaying = false; }
        }

        private static void CheckMainLayout(MenuIntegrationController menu, GameObject main)
        {
            Canvas.ForceUpdateCanvases();
            var group = Field<GameObject>(menu, "mainButtons");
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)group.transform);
            var root = (RectTransform)main.transform;
            var controls = group.GetComponentsInChildren<Button>().Select(button => (RectTransform)button.transform).ToArray();
            var rectangles = controls.Select(control => BoundsIn(root, control)).ToArray();
            for (int i = 0; i < rectangles.Length; i++)
            {
                Check(root.rect.Contains(rectangles[i].min) && root.rect.Contains(rectangles[i].max), "botao cabe dentro do painel");
                for (int j = i + 1; j < rectangles.Length; j++) Check(!rectangles[i].Overlaps(rectangles[j]), "botoes nao se sobrepoem");
            }
            var title = BoundsIn(root, (RectTransform)Field<GameObject>(menu, "mainTitle").transform);
            var summary = BoundsIn(root, (RectTransform)Field<TMP_Text>(menu, "selectedSummary").transform);
            foreach (var rectangle in rectangles)
            {
                Check(!rectangle.Overlaps(title), "titulo nao cobre botao");
                Check(!rectangle.Overlaps(summary), "resumo nao cobre botao");
            }
        }

        private static Rect BoundsIn(RectTransform root, RectTransform element)
        {
            var corners = new Vector3[4];
            element.GetWorldCorners(corners);
            var points = corners.Select(root.InverseTransformPoint).ToArray();
            return Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y), points.Max(point => point.x), points.Max(point => point.y));
        }

        private static async Task WaitForRoster(ParticipantSelectionUI ui)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (ui.IsRefreshing && DateTime.UtcNow < deadline) await Task.Yield();
            Check(!ui.IsRefreshing, "consulta de teste termina dentro do prazo");
        }

        private static T Field<T>(object instance, string name)
            => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        private static void Authenticate(MenuIntegrationController menu, SupabaseAuthSession session)
            => typeof(MenuIntegrationController).GetMethod("OnAutomaticAuthenticated", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, new object[] { session });
        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            checks++;
        }
        private static string FixtureJwt()
        {
            string json = "{\"role\":\"authenticated\",\"sub\":\"00000000-0000-0000-0000-000000000001\",\"exp\":" + DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() + "}";
            return "fixture." + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".fixture";
        }
        private static void Finish()
        {
            int result = SessionState.GetInt(ResultKey, 1);
            SessionState.SetBool(RunningKey, false);
            AssetDatabase.DeleteAsset(TestScene);
            EditorApplication.Exit(result);
        }

        private sealed class RosterFixture : ISupabaseReadClient
        {
            public const string First = "00000000000000000000000000000001";
            public const string Second = "00000000000000000000000000000002";
            public string Rows = "[{\"participant_code\":\"" + First + "\",\"display_name\":\"Pessoa teste\"},{\"participant_code\":\"" + Second + "\",\"display_name\":\"Pessoa teste\"}]";
            public bool Fail;
            public TaskCompletionSource<string> Pending;
            public Task<string> GetAsync(string query, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Fail) throw new SupabaseRequestException(SupabaseFailure.Connection, "Falha de rede simulada.");
                if (query.Contains("participant_code=gt.")) return Task.FromResult("[]");
                return Pending != null ? Pending.Task : Task.FromResult(Rows);
            }
        }
    }
}
