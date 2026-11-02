using System.Collections.Generic;
using MainProject.Trindade.Core.Scripts.ScriptableObjects;
using UnityEngine;

namespace MainProjeto.Core.Scripts.ScriptableObjects
{
    [CreateAssetMenu(fileName = "NewQuest", menuName = "Projeto/Quest", order = 0)]
    public class Quest : ScriptableObject
    {
        public string questTitle;                   // titulo no Main Title do Canvas
        public List<Task> tasks = new List<Task>(); // tarefas a serem adicionadas

    }
}