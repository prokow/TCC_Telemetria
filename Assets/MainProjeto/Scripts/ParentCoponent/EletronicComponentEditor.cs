#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.Interaction.Toolkit.Interactables;
using UnityEngine;

//true indica que essa regra vale para o EletronicComponent e todos os filhos dele
namespace MainProjeto.Scripts.ParentCoponent
{
    [CustomEditor(typeof(EletronicComponent), true)]
    public class EletronicComponentEditor : XRGrabInteractableEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
        
            serializedObject.Update();  // irá ler os valores atuais

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Limite de movimento no mundo", EditorStyles.boldLabel);
            var movementArea = serializedObject.FindProperty("movementArea");
            EditorGUILayout.PropertyField(movementArea, new GUIContent("Área permitida (Box Collider)"));
            if (movementArea.objectReferenceValue != null)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("outsideAreaAction"), new GUIContent("Ao sair da área"));
                var area = movementArea.objectReferenceValue as BoxCollider;
                var component = target as EletronicComponent;
                if (area != null && component != null && area.transform.IsChildOf(component.transform))
                    EditorGUILayout.HelpBox("A caixa deve ficar em outro GameObject, fora da hierarquia móvel da peça. Este limite será ignorado.", MessageType.Warning);
                else if (area != null && !area.isTrigger)
                    EditorGUILayout.HelpBox("Ative Is Trigger na caixa para que ela não vire uma barreira física.", MessageType.Warning);
                EditorGUILayout.HelpBox("Peças conectadas à protoboard são ignoradas. O limite usa o pivô da peça e só atua depois que ela entrou na área. Sem referência, o comportamento atual é preservado.", MessageType.Info);
            }
        
            //EditorGUILayout.Space();
            EditorGUILayout.LabelField("Escolha o eixo que deseja rotacionar", EditorStyles.boldLabel);
        
            // pegam as variáveis que são necessárias
            SerializedProperty canRotate = serializedObject.FindProperty("canRotateComponent");
            EditorGUILayout.PropertyField(canRotate, new GUIContent("Can Rotate Component"));
        
            // caso o can rotate for true ele aparece a caixinha do eixo para onde ele pode rotacionar
            if (canRotate != null && canRotate.boolValue == true)
            {
                SerializedProperty axis = serializedObject.FindProperty("_rotationToUse");
                EditorGUILayout.PropertyField(axis, new GUIContent("Rotation Axis")); 
                
                SerializedProperty prefabRot = serializedObject.FindProperty("prefabRotacao");
                EditorGUILayout.PropertyField(prefabRot, new GUIContent("Prefab Rotação"));

                SerializedProperty altura = serializedObject.FindProperty("alturaDica");
                EditorGUILayout.PropertyField(altura, new GUIContent("Altura Dica"));
            }
        
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Conexão Automática", EditorStyles.boldLabel);
        
            SerializedProperty autoConnectOnAwake = serializedObject.FindProperty("autoConnectOnAwake");
            EditorGUILayout.PropertyField(autoConnectOnAwake, new GUIContent("Auto Connect On Awake"));
        
            if (autoConnectOnAwake != null && autoConnectOnAwake.boolValue == true)
            {
                SerializedProperty autoConnectPin = serializedObject.FindProperty("autoConnectPin");
                EditorGUILayout.PropertyField(autoConnectPin, new GUIContent("Auto Connect Pin"));
            
                GUI.enabled = false;
                SerializedProperty isConnected = serializedObject.FindProperty("isAutoConnected");
                EditorGUILayout.PropertyField(isConnected, new GUIContent("Is Auto Connected (Status)"));
                GUI.enabled = true;
            }
        
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Limites Especificos", EditorStyles.boldLabel);
        
            SerializedProperty hasSpecificLimits = serializedObject.FindProperty("hasSpecificLimits");
            EditorGUILayout.PropertyField(hasSpecificLimits, new GUIContent("Limites Especificos"));

            if (hasSpecificLimits != null && hasSpecificLimits.boolValue == true)
            {
                EditorGUILayout.LabelField("Eixo X", EditorStyles.boldLabel);
                SerializedProperty limitMinX = serializedObject.FindProperty("_limitMinX");
                EditorGUILayout.PropertyField(limitMinX, new GUIContent("Limit Min X"));
            
                SerializedProperty limitMaxX = serializedObject.FindProperty("_limitMaxX");
                EditorGUILayout.PropertyField(limitMaxX, new GUIContent("Limit Max X"));
            
                EditorGUILayout.LabelField("Eixo Y", EditorStyles.boldLabel);
                SerializedProperty limitMinY = serializedObject.FindProperty("_limitMinY");
                EditorGUILayout.PropertyField(limitMinY, new GUIContent("Limit Min Y"));
            
                SerializedProperty limitMaxY = serializedObject.FindProperty("_limitMaxY");
                EditorGUILayout.PropertyField(limitMaxY, new GUIContent("Limit Max Y"));
            }
        
            // salva caso altere o valor do inspector
            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif