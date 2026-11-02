using System;
using System.Collections.Generic;
using System.Linq;
using MainProjeto.Scripts.Protoboard_Interactor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace MainProjeto.Scripts.ParentCoponent
{
    public enum OutsideComponentAreaAction
    {
        [InspectorName("Voltar à última posição válida")]
        ReturnToLastValidPosition,
        [InspectorName("Soltar e cair")]
        ReleaseAndFall
    }

    // Independente dos callbacks de fisica: nao exige triggers nem conta colliders da peca.
    public sealed class ComponentBoundaryState
    {
        private bool entered;
        private bool handledOutside;

        public void Reset() { entered = false; handledOutside = false; }

        public bool ShouldRecover(bool inside, bool connectedToBoard, bool enabled)
        {
            if (!enabled) return false;
            if (inside) { entered = true; handledOutside = false; return false; }
            if (!entered || connectedToBoard || handledOutside) return false;
            handledOutside = true;
            return true;
        }

        // O ponto ja esta no espaco local da caixa: respeita rotacao, escala e center.
        public static bool ContainsLocalPoint(Vector3 point, Vector3 center, Vector3 size)
        {
            const float tolerance = 0.00001f;
            return Math.Abs(point.x - center.x) <= Math.Abs(size.x) * 0.5f + tolerance
                && Math.Abs(point.y - center.y) <= Math.Abs(size.y) * 0.5f + tolerance
                && Math.Abs(point.z - center.z) <= Math.Abs(size.z) * 0.5f + tolerance;
        }
    }

    // Um estado por EletronicComponent. Nao cria GameObjects ou novos colliders na peca.
    internal sealed class ElectronicComponentMovementLimit
    {
        private readonly EletronicComponent component;
        private readonly Rigidbody body;
        private readonly Transform pivot;
        private readonly ComponentBoundaryState state = new ComponentBoundaryState();
        private readonly List<IXRSelectInteractor> blockedHands = new List<IXRSelectInteractor>();
        private BoxCollider currentArea;
        private Vector3 validPosition;
        private Quaternion validRotation, validPivotRotation;
        private bool returnKinematic, returnGravity;
        private OutsideComponentAreaAction pendingAction;
        public bool PendingDetach { get; private set; }

        public ElectronicComponentMovementLimit(EletronicComponent component, Rigidbody body, Transform pivot)
        {
            this.component = component; this.body = body; this.pivot = pivot;
            CaptureBodySettings();
        }

        private void CaptureBodySettings()
        {
            if (body == null) return;
            returnKinematic = body.isKinematic;
            returnGravity = body.useGravity;
        }

        public void BeforeSelect(IXRSelectInteractor interactor)
        {
            if (interactor is ProtoboardInteractor || component.isSelected) return;
            PendingDetach = false;
            CaptureBodySettings();
        }

        public bool AllowsSelection(IXRSelectInteractor interactor)
        {
            // Uma saida forcada nao pode ser desfeita pelo mesmo grip ainda pressionado.
            for (int i = blockedHands.Count - 1; i >= 0; i--)
                if (blockedHands[i] == null
                    || (blockedHands[i] is UnityEngine.Object unityInteractor && unityInteractor == null)
                    || !blockedHands[i].isSelectActive) blockedHands.RemoveAt(i);
            return !blockedHands.Contains(interactor);
        }

        public void Tick(BoxCollider area, OutsideComponentAreaAction action)
        {
            if (currentArea != area) { currentArea = area; state.Reset(); }
            if (area == null || !area.enabled || !area.gameObject.activeInHierarchy
                || area.transform.IsChildOf(component.transform) || !component.IsOriginCircuitActive) return;

            bool connected = false;
            foreach (var interactor in component.interactorsSelecting)
                if (interactor is ProtoboardInteractor) { connected = true; break; }

            bool inside = ComponentBoundaryState.ContainsLocalPoint(
                area.transform.InverseTransformPoint(component.transform.position), area.center, area.size);
            if (inside)
            {
                validPosition = component.transform.position;
                validRotation = component.transform.rotation;
                if (pivot != null) validPivotRotation = pivot.localRotation;
            }
            if (!state.ShouldRecover(inside, connected, true)) return;

            // SelectCancel dispara a limpeza normal do XR ANTES de mover a peca.
            // Nunca cancelar um ProtoboardInteractor nem chamar InstantConnect aqui.
            var selecting = component.interactorsSelecting.ToArray();
            if (selecting.Length > 0 && component.interactionManager == null) return;
            pendingAction = action;
            PendingDetach = selecting.Length > 0;
            foreach (var interactor in selecting)
            {
                if (!blockedHands.Contains(interactor)) blockedHands.Add(interactor);
                component.interactionManager.SelectCancel(interactor, component);
            }
            if (component == null || !component.isActiveAndEnabled || !component.IsOriginCircuitActive) return;

            if (action == OutsideComponentAreaAction.ReturnToLastValidPosition)
            {
                component.transform.SetPositionAndRotation(validPosition, validRotation);
                if (pivot != null) pivot.localRotation = validPivotRotation;
                if (body != null) { body.position = validPosition; body.rotation = validRotation; }
            }
            ApplyBodySettings();
        }

        private void ApplyBodySettings()
        {
            if (body == null) return;
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            bool fall = pendingAction == OutsideComponentAreaAction.ReleaseAndFall;
            body.useGravity = fall || returnGravity;
            body.isKinematic = !fall && returnKinematic;
        }

        public void FinishDetach()
        {
            ApplyBodySettings();
            PendingDetach = false;
        }
    }
}
