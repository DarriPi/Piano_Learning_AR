using System;
using UnityEditor;

namespace PianoLearningCore
{
    /// <summary>
    /// Import settings for the Help slideshow pictures in <see cref="HelpSlideshowBuilder.MediaFolder"/>.
    /// The default texture import would stretch a 16:9 screenshot to a power-of-two square and add
    /// mipmaps, both of which soften the picture on a UI panel viewed straight on. As UI sprites
    /// they keep their real size and stay sharp.
    /// </summary>
    public class TutorialMediaImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(HelpSlideshowBuilder.MediaFolder + "/", StringComparison.OrdinalIgnoreCase))
                return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 2048;
        }
    }
}
