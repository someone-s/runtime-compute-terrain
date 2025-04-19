using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using GLTFast.Schema;

public class ModelLoader : GltfAssetBase
{
    InstantiationSettings instantiationSettings;

    private void Awake()
    {
        ImportSettings = new ImportSettings()
        {
            NodeNameMethod = NameImportMethod.Original,
            AnimationMethod = AnimationMethod.Playables,
            GenerateMipMaps = false,
            TexturesReadable = false,
            DefaultMinFilterMode = Sampler.MinFilterMode.Linear,
            DefaultMagFilterMode = Sampler.MagFilterMode.Linear,
            AnisotropicFilterLevel = 1
        };

        instantiationSettings = new InstantiationSettings()
        {
            Mask = ComponentType.Mesh | ComponentType.Animation,
            Layer = -1,
            SkinUpdateWhenOffscreen = false,
            LightIntensityFactor = 1f,
            SceneObjectCreation = SceneObjectCreation.Always
        };
    }

    public async Task<bool> Load(string filePath)
    {
        string gltfUrl = $"file://{filePath}"; // url always use '/' not '\'
        var success = await base.Load(gltfUrl);
        if (!success) return false;
        
        var instantiator = new GameObjectInstantiator(Importer, transform, settings: instantiationSettings);
        success = await InstantiateScene(0, instantiator);
        if (!success) return false;

        instantiator.SceneTransform.gameObject.SetActive(false);

        return true;
    }

    protected override IInstantiator GetDefaultInstantiator(ICodeLogger logger)
    {
        return new GameObjectInstantiator(Importer, transform, logger, instantiationSettings);
    }

    public override void ClearScenes() {}
}
