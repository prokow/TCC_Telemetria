using System.Collections;
using System.Collections.Generic;
using MainProject.Trindade.Core.Scripts.ScriptableObjects;
using MainProjeto.Core.Scripts.Model;
using MainProjeto.Core.Scripts.ScriptableObjects;
using UnityEngine;
using UnityEngine.Events;

namespace MainProjeto.Robot.Scripts
{
    public class RobotNarrativeController : MonoBehaviour
    {
        [Header("Sequência de Falas do Robô")]
        [SerializeField] private List<RobotFeedbackData> falas;
        [SerializeField] private float tempoEntreFalas = 20f;
        
        [Header("Controle do Usuário")]
        [Tooltip("Se marcado, o robô para ao fim do áudio e espera o jogador chamar ProximaFala(). Se desmarcado, avança sozinho (mas o jogador ainda pode pular/voltar).")]
        [SerializeField] private bool aguardarBotaoDoUsuario = false;

        [Header("Dependências")]
        [Tooltip("Tarefa concluida pela narrativa ou entregue ao botao como ultima tarefa pendente.")]
        [SerializeField] private Task falaRoboTask;
        [Header("Concluir a tarefa automaticamente ao terminar a narrativa")]
        [SerializeField] private bool autoConcluir = false;
        [Header("Botao fisico usado quando Auto Concluir esta desmarcado")]
        [SerializeField] private NextQuestButton botaoProximaQuest;
        [Header("Opcional: evento ao iniciar a narrativa")]
        [SerializeField] private UnityEvent onInitiateNarrative;

        private bool _isRoboFala;
        private int _indiceAtual = 0;
        private Coroutine _rotinaEsperaFala;
        private QuestSystemModel model;
        private Quest narrativeQuest;

        private void OnEnable()
        {
            BindModel();
            
            // Se inscreve para escutar os botões do robô
            RobotNarrativeBridge.OnNextPressed += ProximaFala;
            RobotNarrativeBridge.OnPrevPressed += VoltarFala;
        }
        
        private void OnDisable() 
        { 
            StopAllCoroutines(); 
            UnbindModel(); 
            _isRoboFala = false; 
            narrativeQuest = null; 
            
            // Para de escutar os botões quando o circuito for desligado
            RobotNarrativeBridge.OnNextPressed -= ProximaFala;
            RobotNarrativeBridge.OnPrevPressed -= VoltarFala;
        }
        private void Start() => BindModel();
        
        private void BindModel()
        {
            var current = QuestSystemModel.Instance;
            if (current != model)
            {
                UnbindModel(); 
                model = current;
                if (model != null)
                {
                    model.onTaskComplete.AddListener(VerificarDependencias);
                    model.onQuestLoaded.AddListener(OnQuestLoaded);
                }
            }
            TryStartNarrative();
        }
        
        private void UnbindModel()
        {
            if (model != null)
            {
                model.onTaskComplete.RemoveListener(VerificarDependencias);
                model.onQuestLoaded.RemoveListener(OnQuestLoaded);
            }
            model = null;
        }
        
        private void OnDestroy() => UnbindModel();

        private void OnQuestLoaded()
        {
            StopAllCoroutines(); _isRoboFala = false; narrativeQuest = null; TryStartNarrative();
        }
        
        private void VerificarDependencias(Task _) => TryStartNarrative();
        
        private void TryStartNarrative()
        {
            if (!isActiveAndEnabled || _isRoboFala || model == null || falaRoboTask == null) return;
            if (!QuestAdvanceRules.CanStartNarrative(model.IsTaskAvailable(falaRoboTask), autoConcluir, model.CanPrepareFinalNarrative(falaRoboTask))) return;
            
            _isRoboFala = true; 
            narrativeQuest = model.quest;
            onInitiateNarrative?.Invoke();
            
            if (IsCurrentNarrative()) StartCoroutine(PlayNarrativeRoutine());
        }
        
        private bool IsCurrentNarrative() => isActiveAndEnabled && model != null && model.quest == narrativeQuest;
        
        private IEnumerator PlayNarrativeRoutine()
        {
            yield return new WaitUntil(() => !IsCurrentNarrative() || !model.IsTransitionInProgress);
            if (!IsCurrentNarrative()) yield break;
            
            yield return new WaitUntil(() => RobotFeedbackManager.Instance != null || !IsCurrentNarrative());
            if (!IsCurrentNarrative()) yield break;
            
            yield return new WaitForSeconds(1f);
            
            if (falas == null || falas.Count == 0)
            { 
                Debug.LogWarning("Narrativa sem falas: confira o prefab.", this); 
                yield break; 
            }

            _indiceAtual = 0;
            TocarFalaAtual();
        }
        
        public void ProximaFala()
        {
            if (!_isRoboFala || !IsCurrentNarrative()) return;

            if (_rotinaEsperaFala != null) StopCoroutine(_rotinaEsperaFala);

            _indiceAtual++;
            
            if (_indiceAtual < falas.Count)
            {
                TocarFalaAtual();
            }
            else
            {
                FinalizarNarrativa();
            }
        }

        public void VoltarFala()
        {
            if (!_isRoboFala || !IsCurrentNarrative()) return;

            if (_rotinaEsperaFala != null) StopCoroutine(_rotinaEsperaFala);

            if (_indiceAtual > 0)
            {
                _indiceAtual--;
            }
            
            // Toca a fala anterior (ou repete a primeira se já estava no 0)
            TocarFalaAtual();
        }

        private void TocarFalaAtual()
        {
            if (!IsCurrentNarrative()) return;

            var fala = falas[_indiceAtual];
            if (fala == null || fala.sound == null)
            { 
                Debug.LogWarning($"Narrativa com fala/audio ausente no índice {_indiceAtual}.", this); 
                return; 
            }
            
            RobotFeedbackManager.Instance.TriggerFeedback(fala);
            
            float waitTime = fala.sound.length / Mathf.Max(0.1f, Mathf.Abs(fala.pitch));
            _rotinaEsperaFala = StartCoroutine(AguardarFimDaFala(waitTime + Mathf.Max(0f, tempoEntreFalas)));
            
            if (_indiceAtual == falas.Count - 1)
            {
                LiberarProximaTarefa();
            }
        }

        private IEnumerator AguardarFimDaFala(float tempoEspera)
        {
            yield return new WaitForSeconds(tempoEspera);

            // Se não estiver no modo manual exclusivo, avança sozinho quando o tempo de fala + pausa acabar
            if (!aguardarBotaoDoUsuario)
            {
                ProximaFala();
            }
        }

        private void FinalizarNarrativa()
        {
            if (!IsCurrentNarrative()) return;
            
            _isRoboFala = false;

            if (autoConcluir) 
                model.CompleteTask(falaRoboTask);
            else if (model.CanAdvanceAfterTask(falaRoboTask) && botaoProximaQuest != null)
                botaoProximaQuest.TurnOnButton(falaRoboTask);
        }
        
        

        private void LiberarProximaTarefa()
        {
            // Conclui a Task da narrativa no QuestSystemModel
            if (model != null && narrativeQuest != null)
            {
                model.CompleteTask(falaRoboTask);
                Debug.Log("Última fala iniciada: Próxima Task liberada para interação!");
            }
        }
    }
}
