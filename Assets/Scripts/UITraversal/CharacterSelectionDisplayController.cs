using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class CharacterSelectionDisplayEntry
{
    [SerializeField] private TraverseTarget target;
    [SerializeField] private GameObject illustration;
    [SerializeField] private GameObject description;

    public TraverseTarget Target => target;
    public GameObject Illustration => illustration;
    public GameObject Description => description;
}

[DisallowMultipleComponent]
public sealed class CharacterSelectionDisplayController : MonoBehaviour
{
    [SerializeField] private TraverseCursor cursor;
    [SerializeField] private List<CharacterSelectionDisplayEntry> entries =
        new List<CharacterSelectionDisplayEntry>();
    [SerializeField] private bool hideAllWhenNoTarget = true;

    public TraverseTarget DisplayedTarget { get; private set; }

    private void OnEnable()
    {
        SubscribeCursor();
        RefreshDisplay();
    }

    private void OnDisable()
    {
        UnsubscribeCursor();
    }

    public void RefreshDisplay()
    {
        ShowTarget(cursor != null ? cursor.CurrentTarget : null);
    }

    public void ShowTarget(TraverseTarget target)
    {
        CharacterSelectionDisplayEntry matchingEntry = FindEntry(target);
        if (matchingEntry == null && target != null && !hideAllWhenNoTarget)
        {
            return;
        }

        SetAllDisplaysActive(false);
        DisplayedTarget = null;

        if (matchingEntry == null)
        {
            return;
        }

        SetActive(matchingEntry.Illustration, true);
        SetActive(matchingEntry.Description, true);
        DisplayedTarget = target;
    }

    private CharacterSelectionDisplayEntry FindEntry(TraverseTarget target)
    {
        if (target == null)
        {
            return null;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            CharacterSelectionDisplayEntry entry = entries[i];
            if (entry != null && entry.Target == target)
            {
                return entry;
            }
        }

        return null;
    }

    private void SetAllDisplaysActive(bool active)
    {
        HashSet<GameObject> handledObjects = new HashSet<GameObject>();
        for (int i = 0; i < entries.Count; i++)
        {
            CharacterSelectionDisplayEntry entry = entries[i];
            if (entry == null)
            {
                continue;
            }

            SetActiveOnce(entry.Illustration, active, handledObjects);
            SetActiveOnce(entry.Description, active, handledObjects);
        }
    }

    private void SubscribeCursor()
    {
        if (cursor != null)
        {
            cursor.CurrentTargetChanged -= HandleCurrentTargetChanged;
            cursor.CurrentTargetChanged += HandleCurrentTargetChanged;
        }
    }

    private void UnsubscribeCursor()
    {
        if (cursor != null)
        {
            cursor.CurrentTargetChanged -= HandleCurrentTargetChanged;
        }
    }

    private void HandleCurrentTargetChanged(TraverseTarget target)
    {
        ShowTarget(target);
    }

    private static void SetActiveOnce(
        GameObject target,
        bool active,
        HashSet<GameObject> handledObjects
    )
    {
        if (target != null && handledObjects.Add(target))
        {
            SetActive(target, active);
        }
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    private void OnValidate()
    {
        if (cursor == null)
        {
            Debug.LogWarning(
                "Character display controller requires a TraverseCursor reference.",
                this
            );
        }

        HashSet<TraverseTarget> targets = new HashSet<TraverseTarget>();
        for (int i = 0; i < entries.Count; i++)
        {
            CharacterSelectionDisplayEntry entry = entries[i];
            if (entry == null)
            {
                continue;
            }

            if (entry.Target == null
                || entry.Illustration == null
                || entry.Description == null)
            {
                Debug.LogWarning(
                    $"Character display entry {i} has a missing reference.",
                    this
                );
            }
            else if (!targets.Add(entry.Target))
            {
                Debug.LogWarning(
                    $"Character display entry {i} uses a duplicate target.",
                    this
                );
            }
        }
    }
}
