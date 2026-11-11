using System;
using UnityEngine;

namespace MainProjeto.Robot.Scripts
{
    public class RobotNarrativeBridge : MonoBehaviour
    {
        public static event Action OnNextPressed;
        public static event Action OnPrevPressed;

        // Funções para serem chamadas pelos botões no Inspector
        public void AcionarProximaFala()
        {
            OnNextPressed?.Invoke();
        }

        public void AcionarVoltarFala()
        {
            OnPrevPressed?.Invoke();
        }
    }
}
