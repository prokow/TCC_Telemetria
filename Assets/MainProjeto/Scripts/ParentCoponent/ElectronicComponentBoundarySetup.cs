#if UNITY_EDITOR
using System.Linq;
using MainProjeto.Scripts.Protoboard_Interactor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MainProjeto.Scripts.ParentCoponent
{
    public static class ElectronicComponentBoundarySetup
    {
        [MenuItem("Tools/Circuitos VR/Criar area de movimento das pecas")]
        public static void CreateArea()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = SceneManager.GetActiveScene();
            var components = Object.FindObjectsByType<EletronicComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(component => component.gameObject.scene == scene).ToArray();
            if (components.Length == 0) { Debug.LogWarning("A cena ativa nao possui EletronicComponent."); return; }

            // Reutiliza a caixa ja atribuida; nao cria volumes duplicados ao repetir o comando.
            BoxCollider area = null;
            foreach (var component in components)
            {
                var assigned = new SerializedObject(component).FindProperty("movementArea").objectReferenceValue as BoxCollider;
                if (assigned != null && assigned.gameObject.scene == scene && !assigned.transform.IsChildOf(component.transform))
                { area = assigned; break; }
            }
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Configurar limite de movimento das pecas");
            if (area == null)
            {
                Bounds bounds = new Bounds(components[0].transform.position, Vector3.zero);
                foreach (var component in components) bounds.Encapsulate(component.transform.position);
                foreach (var board in Object.FindObjectsByType<ProtoboardInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (board.gameObject.scene == scene) bounds.Encapsulate(board.transform.position);
                bounds.Expand(1.5f); // Margem inicial de 75 cm por face; ajuste com Edit Collider.
                var root = new GameObject("AreaPermitidaComponentes") { layer = 2 }; // Ignore Raycast
                SceneManager.MoveGameObjectToScene(root, scene);
                Undo.RegisterCreatedObjectUndo(root, "Criar caixa de movimento");
                root.transform.position = bounds.center;
                area = Undo.AddComponent<BoxCollider>(root);
                area.isTrigger = true;
                area.size = bounds.size;
            }
            int assignedCount = 0;
            foreach (var component in components)
            {
                var serialized = new SerializedObject(component);
                var field = serialized.FindProperty("movementArea");
                if (field.objectReferenceValue != null) continue; // Preserva limites individuais existentes.
                field.objectReferenceValue = area;
                serialized.ApplyModifiedProperties();
                assignedCount++;
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = area.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log($"Area de movimento: {assignedCount} pecas configuradas (inclui quests inativas). Ajuste Edit Collider e salve a cena. Ctrl+Z desfaz.", area);
        }
    }
}
#endif
