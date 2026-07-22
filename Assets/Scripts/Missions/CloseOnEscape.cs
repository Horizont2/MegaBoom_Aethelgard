using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Escape → invoke this Button. Stack-aware: when several panels with
// CloseOnEscape are open simultaneously, ONLY the top-most (registered
// last) fires per press. The old behaviour let every instance react to
// the same key, so a two-panel stack collapsed in one keystroke.
[RequireComponent(typeof(Button))]
public class CloseOnEscape : MonoBehaviour
{
    // Insertion-order stack of currently-active CloseOnEscape components.
    // Ordered so the LAST enabled one is on top — the natural mental
    // model for a panel stack.
    private static readonly List<CloseOnEscape> s_stack = new List<CloseOnEscape>();

    private Button closeButton;

    private void Awake()
    {
        closeButton = GetComponent<Button>();
    }

    private void OnEnable()
    {
        // Reinsert as the top of the stack — a panel that just opened is
        // now the one Escape should close.
        s_stack.Remove(this);
        s_stack.Add(this);
    }

    private void OnDisable()
    {
        s_stack.Remove(this);
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        // Only the top of the stack acts. Everyone else short-circuits so
        // a single Escape can never cascade two panels closed at once.
        if (s_stack.Count == 0 || s_stack[s_stack.Count - 1] != this) return;
        closeButton.onClick.Invoke();
    }
}