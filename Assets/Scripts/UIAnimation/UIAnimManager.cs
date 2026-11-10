using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

[Serializable]
public sealed class UIAnimElementState
{
    [SerializeField] private UIAnimElement element;
    [SerializeField] private bool active;

    public UIAnimElement Element => element;
    public bool Active => active;
}

[Serializable]
public sealed class UIAnimStateDefinition
{
    [SerializeField] private string id;
    [SerializeField] private List<UIAnimElementState> elements = new();

    public string Id => id;
    public IReadOnlyList<UIAnimElementState> Elements => elements;
}

[Serializable]
public sealed class UIAnimStateChangedEvent : UnityEvent<string> { }

[DisallowMultipleComponent]
public sealed class UIAnimManager : MonoBehaviour
{
    [Header("State Catalogue")]
    [SerializeField] private List<UIAnimStateDefinition> states = new();

    [Header("Events")]
    [FormerlySerializedAs("onStateChanged")]
    [SerializeField] private UIAnimStateChangedEvent onStateApplied;

    public IReadOnlyList<UIAnimStateDefinition> States => states;
    public int StateCount => states.Count;

    internal bool ApplyState(int stateIndex, bool immediate = false)
    {
        if (!IsValidStateIndex(stateIndex))
        {
            Debug.LogError($"UI animation state index {stateIndex} is invalid.", this);
            return false;
        }

        UIAnimStateDefinition state = states[stateIndex];
        ApplyElements(state, immediate);
        onStateApplied?.Invoke(state.Id);
        return true;
    }

    internal bool TryApplyState(string stateId, bool immediate = false)
    {
        int stateIndex = FindStateIndex(stateId);
        if (stateIndex < 0)
        {
            Debug.LogError($"UI animation state '{stateId}' was not found.", this);
            return false;
        }

        return ApplyState(stateIndex, immediate);
    }

    public bool HasState(string stateId)
    {
        return FindStateIndex(stateId) >= 0;
    }

    public int FindStateIndex(string stateId)
    {
        if (string.IsNullOrWhiteSpace(stateId))
        {
            return -1;
        }

        int foundIndex = -1;
        for (int i = 0; i < states.Count; i++)
        {
            UIAnimStateDefinition state = states[i];
            if (state != null && string.Equals(state.Id, stateId, StringComparison.Ordinal))
            {
                if (foundIndex >= 0)
                {
                    return -1;
                }
                foundIndex = i;
            }
        }
        return foundIndex;
    }

    public string GetStateId(int stateIndex)
    {
        return IsValidStateIndex(stateIndex) ? states[stateIndex].Id : string.Empty;
    }

    public bool IsValidStateIndex(int index)
    {
        return index >= 0 && index < states.Count && states[index] != null;
    }

    public void SetElementActive(UIAnimElement element, bool active)
    {
        SetElementActive(element, active, false);
    }

    public void SetElementActive(UIAnimElement element, bool active, bool immediate)
    {
        if (element == null)
        {
            Debug.LogWarning("Cannot change a null UI animation element.", this);
            return;
        }

        element.SetActiveState(active, immediate);
    }

    public void ActivateElement(UIAnimElement element)
    {
        SetElementActive(element, true);
    }

    public void DeactivateElement(UIAnimElement element)
    {
        SetElementActive(element, false);
    }

    public void ToggleElement(UIAnimElement element)
    {
        if (element != null)
        {
            element.Toggle();
        }
    }

    private static void ApplyElements(UIAnimStateDefinition state, bool immediate)
    {
        IReadOnlyList<UIAnimElementState> elements = state.Elements;
        for (int i = 0; i < elements.Count; i++)
        {
            UIAnimElementState elementState = elements[i];
            if (elementState?.Element != null)
            {
                elementState.Element.SetActiveState(elementState.Active, immediate);
            }
        }
    }

    private void OnValidate()
    {
        HashSet<string> usedIds = new(StringComparer.Ordinal);
        for (int i = 0; i < states.Count; i++)
        {
            UIAnimStateDefinition state = states[i];
            if (state == null || string.IsNullOrWhiteSpace(state.Id))
            {
                continue;
            }

            if (!usedIds.Add(state.Id))
            {
                Debug.LogWarning($"Duplicate UI animation state id: '{state.Id}'.", this);
            }

            HashSet<UIAnimElement> usedElements = new();
            IReadOnlyList<UIAnimElementState> elements = state.Elements;
            for (int elementIndex = 0; elementIndex < elements.Count; elementIndex++)
            {
                UIAnimElement element = elements[elementIndex]?.Element;
                if (element != null && !usedElements.Add(element))
                {
                    Debug.LogWarning(
                        $"State '{state.Id}' contains UI element '{element.name}' more than once.",
                        this
                    );
                }
            }
        }
    }
}
