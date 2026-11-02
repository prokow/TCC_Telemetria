using UnityEngine;

namespace MainProject.Trindade.Core.Scripts.ScriptableObjects
{
    [CreateAssetMenu(fileName = "NewTask", menuName = "Projeto/Task", order = 0)]
    public class Task : ScriptableObject
    {
        public new string name;     // nome da tarefa
        public string description;  // o que precisa fazer nesta tarefa
        
        [Header("Dependências")]
        public Task[] dependencies;
    }
}