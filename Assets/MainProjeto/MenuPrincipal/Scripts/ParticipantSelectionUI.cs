using System;
using System.Collections.Generic;
using System.Threading;
using MainProjeto.Telemetria.Participants;
using MainProjeto.Telemetria.Supabase;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MainProjeto.MenuPrincipal.Scripts
{
    public sealed class ParticipantSelectionUI : MonoBehaviour
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private ParticipantListItemUI itemTemplate;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text selectionText;
        [SerializeField] private Button refreshButton;
        [SerializeField] private Button backButton;

        private readonly List<ParticipantListItemUI> rows = new List<ParticipantListItemUI>();
        private ParticipantRosterSync rosterSync;
        private CancellationTokenSource currentQuery;
        private Participant selected;
        private bool canChangeSelection = true;
        private bool navigationLocked;
        public bool HasValidList { get; private set; }
        public bool IsLoading { get; private set; }
        public bool IsRefreshing => currentQuery != null;
        public event Action<Participant> ParticipantChosen;
        public event Action RosterChanged;
        public event Action StateChanged;
        public event Action BackRequested;

        private void Awake()
        {
            itemTemplate.gameObject.SetActive(false);
            refreshButton.onClick.AddListener(RefreshParticipants);
            backButton.onClick.AddListener(GoBack);
            selectionText.richText = false;
        }

        public void Initialize(ParticipantRepository participantRepository) => rosterSync = new ParticipantRosterSync(participantRepository);

        public void InvalidateList()
        {
            CancelRefresh();
            HasValidList = false;
            rosterSync?.Clear();
            foreach (var row in rows)
            {
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
            rows.Clear();
            RefreshScrollLayout(1f);
            UpdateControls();
            StateChanged?.Invoke();
        }

        public bool ContainsParticipant(string code)
        {
            return HasValidList && rows.Exists(row => row.ParticipantCode == code);
        }

        public Participant FindParticipant(string code)
        {
            if (!HasValidList || rosterSync == null) return null;
            foreach (var participant in rosterSync.Snapshot)
                if (participant.ParticipantCode == code) return participant;
            return null;
        }

        public void SetSelection(Participant participant, bool canChange)
        {
            selected = participant;
            canChangeSelection = canChange;
            selectionText.text = participant == null ? "Nenhum participante selecionado." : "Selecionado: " + participant.DisplayName;
            UpdateControls();
        }

        public void SetNavigationLocked(bool locked)
        {
            navigationLocked = locked;
            UpdateControls();
        }

        public void RefreshParticipants() => Refresh(false);
        public void RefreshInBackground()
        {
            if (!IsRefreshing) Refresh(true);
        }

        private async void Refresh(bool background)
        {
            if (navigationLocked) return;
            CancelRefresh();
            if (rosterSync == null)
            {
                ShowMessage("A conexão não está configurada. Contate o responsável pela aplicação.");
                return;
            }

            var cancellation = new CancellationTokenSource();
            currentQuery = cancellation;
            bool silent = background && HasValidList;
            if (!silent) HasValidList = false;
            IsLoading = !silent;
            if (!silent) ShowMessage("Carregando participantes...");
            UpdateControls();
            StateChanged?.Invoke();
            try
            {
                var update = await rosterSync.RefreshAsync(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(currentQuery, cancellation) || this == null) return;
                var participants = update.Participants;
                float scrollPosition = scrollRect.verticalNormalizedPosition;
                bool rebuild = update.Changed || !HasValidList;
                if (rebuild) RenderParticipants(participants);
                HasValidList = true;
                if (rebuild || !silent) RefreshScrollLayout(silent ? scrollPosition : 1f);
                ShowMessage(participants.Count == 0
                    ? "Nenhum participante disponível para esta conta."
                    : "Selecione o participante e volte ao menu para iniciar.");
                RosterChanged?.Invoke();
            }
            catch (OperationCanceledException) { }
            catch (SupabaseRequestException ex)
            {
                if (IsCurrent(cancellation))
                {
                    HasValidList = false;
                    ShowMessage(ex.Failure == SupabaseFailure.AccessDenied
                        ? "Acesso à lista não autorizado. Verifique a autorização do dispositivo."
                        : ex.Failure == SupabaseFailure.Connection
                            ? "Não foi possível conectar. Verifique a rede e toque em Atualizar."
                            : "Não foi possível carregar a lista. Toque em Atualizar para tentar novamente.");
                }
            }
            catch (Exception)
            {
                if (IsCurrent(cancellation))
                {
                    HasValidList = false;
                    ShowMessage("Não foi possível carregar a lista. Contate o responsável pela aplicação.");
                }
            }
            finally
            {
                if (IsCurrent(cancellation))
                {
                    currentQuery = null;
                    IsLoading = false;
                    UpdateControls();
                    StateChanged?.Invoke();
                }
                cancellation.Dispose();
            }
        }

        public void CancelRefresh()
        {
            if (currentQuery == null) return;
            bool wasLoading = IsLoading;
            currentQuery.Cancel();
            currentQuery = null;
            IsLoading = false;
            if (wasLoading) ShowMessage("Consulta interrompida. Toque em Atualizar para tentar novamente.");
            UpdateControls();
            StateChanged?.Invoke();
        }

        public void ShowMessage(string message) => statusText.text = message;

        private void RenderParticipants(IReadOnlyList<Participant> participants)
        {
            var existing = new Dictionary<string, ParticipantListItemUI>(StringComparer.Ordinal);
            foreach (var row in rows) existing.Add(row.ParticipantCode, row);
            rows.Clear();
            foreach (var participant in participants)
            {
                if (!existing.TryGetValue(participant.ParticipantCode, out var row))
                {
                    row = Instantiate(itemTemplate, content, false);
                    row.name = "ParticipantRow";
                }
                else existing.Remove(participant.ParticipantCode);
                row.Bind(participant, value => ParticipantChosen?.Invoke(value));
                rows.Add(row);
                row.transform.SetSiblingIndex(rows.Count);
                row.gameObject.SetActive(true);
            }
            foreach (var row in existing.Values)
            {
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
        }

        private bool IsCurrent(CancellationTokenSource query)
        {
            return this != null && ReferenceEquals(currentQuery, query) && !query.IsCancellationRequested;
        }

        private void RefreshScrollLayout(float position)
        {
            // O tamanho precisa estar atualizado antes de posicionar a lista e a barra.
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)scrollRect.transform);
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = Mathf.Clamp01(position);
        }

        private void UpdateControls()
        {
            refreshButton.interactable = !navigationLocked && !IsLoading;
            backButton.interactable = !navigationLocked;
            foreach (var row in rows)
                row.SetSelection(selected?.ParticipantCode, canChangeSelection && HasValidList && !IsLoading && !navigationLocked);
        }

        private void GoBack() => BackRequested?.Invoke();
        private void OnDisable() => CancelRefresh();
        private void OnDestroy()
        {
            if (refreshButton != null) refreshButton.onClick.RemoveListener(RefreshParticipants);
            if (backButton != null) backButton.onClick.RemoveListener(GoBack);
        }
    }
}
