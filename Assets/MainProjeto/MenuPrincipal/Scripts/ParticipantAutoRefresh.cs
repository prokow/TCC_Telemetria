using UnityEngine;

namespace MainProjeto.MenuPrincipal.Scripts
{
    /// <summary>Atualiza pelo Supabase independentemente de o frontend estar aberto.</summary>
    public sealed class ParticipantAutoRefresh : MonoBehaviour
    {
        [SerializeField] private MenuIntegrationController menu;
        [SerializeField] private ParticipantSelectionUI selectionUI;
        [SerializeField, Min(2f)] private float intervalSeconds = 5f;
        private double nextRefresh;

        private void OnEnable()
        {
            ScheduleNext();
            if (selectionUI != null) selectionUI.RosterChanged += ScheduleNext;
        }

        private void Update()
        {
            if (menu == null || selectionUI == null || !menu.CanRefreshParticipants || selectionUI.IsRefreshing) return;
            if (Time.realtimeSinceStartupAsDouble < nextRefresh) return;
            ScheduleNext();
            selectionUI.RefreshInBackground();
        }

        private void ScheduleNext() => nextRefresh = Time.realtimeSinceStartupAsDouble + Mathf.Max(2f, intervalSeconds);

        private void OnApplicationFocus(bool focused)
        {
            if (focused) nextRefresh = 0;
        }

        private void OnDisable()
        {
            if (selectionUI != null) selectionUI.RosterChanged -= ScheduleNext;
        }
    }
}
