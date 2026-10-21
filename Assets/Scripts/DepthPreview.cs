using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

// Köşede ARCore derinlik haritasını gösterir (hata ayıklama).
public class DepthPreview : MonoBehaviour
{
    public AROcclusionManager occlusion;
    public RawImage image;

    void Update()
    {
        var tex = occlusion != null ? occlusion.environmentDepthTexture : null;
        image.enabled = tex != null;
        if (tex != null) image.texture = tex;
    }
}
