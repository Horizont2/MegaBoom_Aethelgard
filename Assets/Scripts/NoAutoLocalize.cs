using UnityEngine;

// Marker: AutoLocalize skips any TMP/Text carrying this component (or under a
// GameObject that has it). Use it on labels whose text is set dynamically by
// code (e.g. the main-menu Continue/Start button), so AutoLocalize's self-keyed
// translation can't overwrite the code-set value on a language change.
public class NoAutoLocalize : MonoBehaviour { }
