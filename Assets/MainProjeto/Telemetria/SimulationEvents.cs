using System;
using UnityEngine;

namespace MainProjeto.Telemetria
{
    public static class SimulationEvents
    {
        public static event Action<string> OnSessionStarted;
        public static event Action OnSessionEnded;

        public static void TriggerSessionStarted(string sessionID)
        {
            OnSessionStarted?.Invoke(sessionID);
            Debug.Log($"[SimulationEvents] Sessão iniciada: {sessionID}");
        }

        public static void TriggerSessionEnded()
        {
            OnSessionEnded?.Invoke();
            Debug.Log("[SimulationEvents] Sessão encerrada.");
        }
    }
}