#if UNITY_EDITOR

namespace Polyart
{

    using UnityEngine;
    public class PathLODHeightMultiplier : MonoBehaviour
    {
        [Range(0f, 0.1f)]
        public float heightMultiplier = 0.02f;
        private void OnValidate()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                child.localPosition = new Vector3(0f, heightMultiplier * i, 0f);
            }
        }
    }

}
#else

namespace Polyart
{

    using UnityEngine;
    public class PathLODHeightMultiplier : MonoBehaviour
    {
        [Range(0f, 0.1f)]
        public float heightMultiplier = 0.02f;
        private void Start()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                child.localPosition = new Vector3(0f, heightMultiplier * i, 0f);
            }
        }
    }

}

#endif