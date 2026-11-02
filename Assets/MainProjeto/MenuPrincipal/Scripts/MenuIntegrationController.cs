using System;
using MainProjeto.Telemetria.Participants;
using MainProjeto.Telemetria.Sessions;
using MainProjeto.Telemetria.Supabase;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MainProjeto.MenuPrincipal.Scripts
{
    [DisallowMultipleComponent]
    public sealed class MenuIntegrationController : MonoBehaviour
    {
        [SerializeField] private SupabaseConnectionSettings settings;
        [SerializeField] private ParticipantSelectionUI selectionUI;
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject participantPanel;
        [SerializeField] private GameObject mainButtons;
        [SerializeField] private GameObject mainTitle;
        [SerializeField] private Button openParticipantsButton;
        [SerializeField] private Button startButton;
        [SerializeField] private TMP_Text selectedSummary;
        [SerializeField] private OperatorLoginUI loginUI;
        [SerializeField] private GameObject loginPanel;
        [SerializeField] private Button changeAccountButton;
        [SerializeField] private AutomaticOperatorLogin automaticLogin;
        [SerializeField] private string simulatorScene = "SampleScene";
        private ApplicationSessionContext context;
        private bool starting;
        private SupabaseAuthSession authSession;
        private TMP_Text openParticipantsLabel;
        private string authenticationStatus = "Conectando ao cadastro de participantes...";
        private bool IsAuthenticated => authSession != null && !authSession.IsExpired(DateTimeOffset.UtcNow);
        public bool CanRefreshParticipants => isActiveAndEnabled && IsAuthenticated && !starting && context != null && context.Session.CanChangeParticipant;

        private void Awake()
        {
            context = ApplicationSessionContext.EnsureExists();
            // Ao retornar para atender outra pessoa, exigir uma nova escolha explicita.
            // Uma sessao ativa ou com salvamento pendente continua protegida pelo contexto.
            if (automaticLogin != null) context.TryClearSelection();
            selectedSummary.richText = false;
            participantPanel.SetActive(false);
            loginPanel.SetActive(false);
            openParticipantsLabel = openParticipantsButton.GetComponentInChildren<TMP_Text>(true);
            openParticipantsButton.onClick.AddListener(OpenParticipants);
            changeAccountButton.onClick.AddListener(ChangeAccount);
            loginUI.Authenticated += OnAuthenticated;
            loginUI.BackRequested += CloseLogin;
            selectionUI.ParticipantChosen += SelectParticipant;
            selectionUI.RosterChanged += ValidateRoster;
            selectionUI.StateChanged += UpdateStartState;
            selectionUI.BackRequested += CloseParticipants;
            context.Changed += UpdateStartState;
            if (automaticLogin != null)
            {
                automaticLogin.Authenticated += OnAutomaticAuthenticated;
                automaticLogin.StatusChanged += OnAutomaticStatus;
                changeAccountButton.gameObject.SetActive(false);
                selectionUI.ShowMessage(authenticationStatus);
            }
            try
            {
                var connection = settings.CreateConnection();
                selectionUI.Initialize(new ParticipantRepository(new SupabaseService(connection, () => authSession?.AccessToken)));
                loginUI.Initialize(new SupabaseAuthService(new UnitySupabaseAuthTransport(connection)));
            }
            catch (Exception)
            {
                selectionUI.ShowMessage("A conexão não está configurada. Contate o responsável pela aplicação.");
            }
            ShowMainMenu();
        }

        private void Update()
        {
            if (!starting && authSession != null && !IsAuthenticated)
            {
                if (automaticLogin != null)
                {
                    authSession = null;
                    selectionUI.InvalidateList();
                    UpdateStartState();
                    selectionUI.ShowMessage("Reconectando ao cadastro de participantes...");
                    automaticLogin.Restore();
                    return;
                }
                ClearAuthentication();
                OpenLogin();
                loginUI.ShowMessage("Seu acesso expirou. Entre novamente.");
            }
        }

        private void OnAutomaticAuthenticated(SupabaseAuthSession session)
        {
            if (starting) return;
            bool changedAccount = authSession != null && authSession.UserId != session.UserId;
            authSession = session;
            if (changedAccount)
            {
                context.TryClearSelection();
                selectionUI.InvalidateList();
            }
            if (!selectionUI.IsRefreshing) selectionUI.RefreshInBackground();
            UpdateStartState();
        }

        private void OnAutomaticStatus(string message)
        {
            if (starting || IsAuthenticated) return;
            authenticationStatus = message;
            selectionUI.ShowMessage(message);
            UpdateStartState();
        }

        private void ShowParticipantPanel()
        {
            loginPanel.SetActive(false);
            mainPanel.SetActive(false);
            mainButtons.SetActive(false);
            mainTitle.SetActive(false);
            selectedSummary.gameObject.SetActive(false);
            participantPanel.SetActive(true);
        }

        private void ShowMainMenu()
        {
            loginPanel.SetActive(false);
            participantPanel.SetActive(false);
            mainPanel.SetActive(true);
            mainButtons.SetActive(true);
            mainTitle.SetActive(true);
            selectedSummary.gameObject.SetActive(true);
            UpdateStartState();
        }

        // O fluxo de login pode fornecer/renovar o JWT sem salvá-lo em asset ou cena.
        public void SetAccessToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) { ChangeAccount(); return; }
            OnAuthenticated(SupabaseAuthSession.FromAccessToken(token, DateTimeOffset.UtcNow));
        }

        private void OnAuthenticated(SupabaseAuthSession session)
        {
            authSession = session;
            context.TryClearSelection();
            selectionUI.InvalidateList();
            CloseLogin();
            OpenParticipants();
        }

        private void ClearAuthentication()
        {
            authSession = null;
            context.TryClearSelection();
            selectionUI.InvalidateList();
            UpdateStartState();
        }

        private void ChangeAccount()
        {
            if (starting) return;
            if (!context.Session.CanChangeParticipant)
            {
                selectionUI.ShowMessage("Encerre e salve a sessão atual antes de trocar de conta.");
                return;
            }
            ClearAuthentication();
            OpenLogin();
            loginUI.ShowMessage("Entre com a conta do responsável.");
        }

        private void OpenLogin()
        {
            selectionUI.CancelRefresh();
            participantPanel.SetActive(false);
            mainPanel.SetActive(false);
            mainButtons.SetActive(false);
            mainTitle.SetActive(false);
            selectedSummary.gameObject.SetActive(false);
            loginPanel.SetActive(true);
        }

        private void CloseLogin()
        {
            loginUI.CancelLogin();
            ShowMainMenu();
        }

        public void OpenParticipants()
        {
            if (starting) return;
            if (automaticLogin != null)
            {
                ShowParticipantPanel();
                if (!IsAuthenticated)
                {
                    selectionUI.ShowMessage(authenticationStatus);
                    automaticLogin.Restore();
                }
                else if (!selectionUI.HasValidList && !selectionUI.IsRefreshing) selectionUI.RefreshParticipants();
                return;
            }
            if (!IsAuthenticated) { OpenLogin(); return; }
            ShowParticipantPanel();
            if (!selectionUI.HasValidList && !selectionUI.IsLoading) selectionUI.RefreshParticipants();
        }

        public void CloseParticipants()
        {
            if (starting) return;
            // A consulta pode terminar em segundo plano, preservando a escolha ao voltar.
            ShowMainMenu();
        }

        private void SelectParticipant(Participant participant)
        {
            if (starting || !IsAuthenticated || selectionUI.IsLoading || !selectionUI.ContainsParticipant(participant.ParticipantCode)) return;
            if (!context.TrySelect(participant))
                selectionUI.ShowMessage("Encerre e salve a sessão atual antes de trocar de participante.");
        }

        private void ValidateRoster()
        {
            var selected = context.Participants.Selected;
            if (selected != null)
            {
                var current = selectionUI.FindParticipant(selected.ParticipantCode);
                if (current == null) context.TryClearSelection();
                else if (current.DisplayName != selected.DisplayName) context.TrySelect(current);
            }
            UpdateStartState();
        }

        private void UpdateStartState()
        {
            var selected = context.Participants.Selected;
            selectionUI.SetSelection(selected, context.Session.CanChangeParticipant && !starting);
            startButton.interactable = IsAuthenticated && !starting && !selectionUI.IsLoading && context.Session.CanChangeParticipant &&
                                       selected != null && selectionUI.ContainsParticipant(selected.ParticipantCode);
            selectedSummary.text = selected == null ? "Selecione um participante para iniciar." : "Participante: " + selected.DisplayName;
            if (openParticipantsLabel != null) openParticipantsLabel.text = "Selecionar participantes";
            if (!IsAuthenticated) selectedSummary.text = automaticLogin != null ? authenticationStatus : "Entre com a conta do responsável.";
            if (selected != null && !startButton.interactable && !starting)
                selectedSummary.text += context.Session.CanChangeParticipant ? "\nAtualize a lista antes de iniciar." : "\nEncerre e salve a sessão atual.";
        }

        public void StartExperience()
        {
            UpdateStartState();
            if (!startButton.interactable || starting)
            {
                if (!starting) OpenParticipants();
                return;
            }
            if (!Application.CanStreamedLevelBeLoaded(simulatorScene))
            {
                OpenParticipants();
                selectionUI.ShowMessage("A cena do simulador não está disponível nesta instalação.");
                return;
            }
            if (!context.TryPrepareSession()) return;
            starting = true;
            selectionUI.CancelRefresh();
            selectionUI.SetNavigationLocked(true);
            UpdateStartState();
            try
            {
                if (SceneManager.LoadSceneAsync(simulatorScene) == null) throw new InvalidOperationException();
            }
            catch (Exception)
            {
                starting = false;
                selectionUI.SetNavigationLocked(false);
                OpenParticipants();
                selectionUI.ShowMessage("Não foi possível abrir o simulador. Tente novamente.");
                UpdateStartState();
            }
        }

        private void OnDestroy()
        {
            authSession = null;
            if (automaticLogin != null)
            {
                automaticLogin.Authenticated -= OnAutomaticAuthenticated;
                automaticLogin.StatusChanged -= OnAutomaticStatus;
            }
            if (openParticipantsButton != null) openParticipantsButton.onClick.RemoveListener(OpenParticipants);
            if (changeAccountButton != null) changeAccountButton.onClick.RemoveListener(ChangeAccount);
            if (loginUI != null)
            {
                loginUI.Authenticated -= OnAuthenticated;
                loginUI.BackRequested -= CloseLogin;
            }
            if (selectionUI != null)
            {
                selectionUI.ParticipantChosen -= SelectParticipant;
                selectionUI.RosterChanged -= ValidateRoster;
                selectionUI.StateChanged -= UpdateStartState;
                selectionUI.BackRequested -= CloseParticipants;
            }
            if (context != null) context.Changed -= UpdateStartState;
        }
    }
}
