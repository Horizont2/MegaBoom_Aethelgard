namespace Polyart {

    using UnityEngine;

    public class OceanUnderwaterPostProcessing : MonoBehaviour
    {
        public OceanTool oceanTool;

        // This function will now fire because it's on the Camera
        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (oceanTool == null)
            {
                Graphics.Blit(source, destination);

                Debug.LogWarning("Ocean Tool is NOT Valid!");
                return;
            }

            oceanTool.ExecuteunderwaterPostProcess(source, destination);
        }
    }

}