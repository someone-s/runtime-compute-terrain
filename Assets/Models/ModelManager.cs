using UnityEngine;

[RequireComponent(typeof(ModelLoader))]
public class ModelManager : MonoBehaviour
{
    private ModelLoader loader;

    private void Awake() => loader = GetComponent<ModelLoader>();

}