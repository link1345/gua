using UnityEngine;

namespace Gua.Unity
{

[DisallowMultipleComponent]
public sealed class GuaId : MonoBehaviour
{
    [SerializeField] private string value = "";
    public string Value { get => value; set => this.value = value ?? ""; }
}

[DisallowMultipleComponent]
public sealed class GuaScreen : MonoBehaviour
{
    [SerializeField] private string value = "";
    public string Value { get => value; set => this.value = value ?? ""; }
}
}
