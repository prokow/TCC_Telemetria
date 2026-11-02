using System;
using MainProjeto.Telemetria.Participants;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MainProjeto.MenuPrincipal.Scripts
{
    public sealed class ParticipantListItemUI : MonoBehaviour
    {
        [SerializeField] private Button selectButton;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text actionText;
        [SerializeField] private Image background;
        private Participant participant;
        private Action<Participant> selected;
        public string ParticipantCode => participant?.ParticipantCode;

        public void Bind(Participant value, Action<Participant> onSelected)
        {
            participant = value;
            selected = onSelected;
            nameText.richText = false;
            nameText.text = value.DisplayName + " (" + value.ParticipantCode.Substring(0, 8) + ")";
            selectButton.onClick.RemoveListener(Select);
            selectButton.onClick.AddListener(Select);
        }

        public void SetSelection(string selectedCode, bool canSelect)
        {
            bool isSelected = participant != null && participant.ParticipantCode == selectedCode;
            background.color = isSelected ? new Color(1f, 0.94f, 0.55f) : Color.white;
            actionText.text = isSelected ? "Selecionado" : "Selecionar";
            selectButton.interactable = canSelect && !isSelected;
        }

        private void Select() => selected?.Invoke(participant);
        private void OnDestroy()
        {
            if (selectButton != null) selectButton.onClick.RemoveListener(Select);
        }
    }
}
