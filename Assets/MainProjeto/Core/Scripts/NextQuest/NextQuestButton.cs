using MainProject.Trindade.Core.Scripts.ScriptableObjects;
using MainProjeto.Core.Scripts.Model;
using QuickOutline.Scripts;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(Outline))]
public class NextQuestButton : XRSimpleInteractable
{
    [Header("Materiais de Estado")]
    [SerializeField] private Material _materialLocked;
    [SerializeField] private Material _materialReady;
    [SerializeField] private Material _materialPressed;

    private Renderer _renderer;
    private Animator _animator;
    private Outline _outline;
    private QuestSystemModel model;
    private readonly QuestAdvanceButtonState state = new QuestAdvanceButtonState();
    public bool IsReadyToAdvance => model != null && state.CanPress(model.quest, model.CanAdvanceAfterTask(state.TaskToken as Task));

    protected override void Awake()
    {
        base.Awake();
        _outline = GetComponent<Outline>();
        if (_outline != null)
        {
            _outline.OutlineMode = Outline.Mode.OutlineAll;
            _outline.OutlineWidth = 8f;
            _outline.OutlineColor = Color.green;
            _outline.enabled = false;
        }
        _animator = GetComponent<Animator>();
        _renderer = GetComponent<Renderer>();
        TurnOffButton();
    }

    protected override void OnEnable() { base.OnEnable(); BindModel(); }
    private void Start() => BindModel();
    protected override void OnDisable()
    {
        if (model != null) model.onQuestLoaded.RemoveListener(TurnOffButton);
        model = null; TurnOffButton();
        base.OnDisable();
    }
    private void BindModel()
    {
        var current = QuestSystemModel.Instance;
        if (current == model) return;
        if (model != null) model.onQuestLoaded.RemoveListener(TurnOffButton);
        model = current; TurnOffButton();
        if (model != null) model.onQuestLoaded.AddListener(TurnOffButton);
    }

    // Preserva a assinatura do UnityEvent serializado na SampleScene.
    public void TurnOnButton() => TurnOnButton(null);
    public void TurnOnButton(Task task)
    {
        BindModel();
        if (!state.Request(model != null ? model.quest : null, task, model != null && model.CanAdvanceAfterTask(task)))
        {
            if (!state.Pressing) TurnOffButton();
            return;
        }
        if (_renderer != null && _materialReady != null) _renderer.material = _materialReady;
        Debug.Log("Botao de proxima quest liberado apos validar as tarefas da fase atual.");
    }
    private void TurnOffButton()
    {
        state.Reset();
        if (_outline != null) _outline.enabled = false;
        if (_renderer != null && _materialLocked != null) _renderer.material = _materialLocked;
    }

    public override bool IsSelectableBy(IXRSelectInteractor interactor) => IsReadyToAdvance && base.IsSelectableBy(interactor);
    public override bool IsHoverableBy(IXRHoverInteractor interactor) => IsReadyToAdvance && base.IsHoverableBy(interactor);
    protected override void OnHoverEntered(HoverEnterEventArgs args)
    { base.OnHoverEntered(args); if (_outline != null && IsReadyToAdvance) _outline.enabled = true; }
    protected override void OnHoverExited(HoverExitEventArgs args)
    { base.OnHoverExited(args); if (_outline != null) _outline.enabled = false; }

    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);
        var activeModel = model;
        if (activeModel == null || !state.TryBeginPress(activeModel.quest, activeModel.CanAdvanceAfterTask(state.TaskToken as Task))) return;
        var activeQuest = activeModel.quest;
        var pendingTask = state.TaskToken as Task;
        try
        {
            if (_animator != null) _animator.SetTrigger("Press");
            if (_renderer != null && _materialPressed != null) _renderer.material = _materialPressed;
            if (_outline != null) _outline.enabled = false;
            if (pendingTask != null) activeModel.CompleteTask(pendingTask);
            // onQuestComplete pode chamar TurnOnButton de forma reentrante: state.Pressing bloqueia.
            if (activeModel != null && activeModel.quest == activeQuest && activeModel.AreAllCurrentTasksDone)
                activeModel.TriggerNextQuestTransition();
        }
        finally { TurnOffButton(); }
    }
}


