using UnityEngine;

[CreateAssetMenu(fileName = "CoinPhysicsMaterial", menuName = "Physics/Coin Physics Material")]
public class CoinPhysicsSettings : ScriptableObject
{
    [Header("物理マテリアル設定")]
    [SerializeField] private PhysicMaterial physicsMaterial;
    
    [Header("推奨値")]
    [SerializeField, Range(0f, 1f)] private float dynamicFriction = 0.6f;
    [SerializeField, Range(0f, 1f)] private float staticFriction = 0.6f;
    [SerializeField, Range(0f, 1f)] private float bounciness = 0.3f;
    [SerializeField] private PhysicMaterialCombine frictionCombine = PhysicMaterialCombine.Average;
    [SerializeField] private PhysicMaterialCombine bounceCombine = PhysicMaterialCombine.Average;
    
    public PhysicMaterial GetPhysicsMaterial()
    {
        if (physicsMaterial == null)
        {
            CreateDefaultMaterial();
        }
        
        return physicsMaterial;
    }
    
    private void CreateDefaultMaterial()
    {
        physicsMaterial = new PhysicMaterial("CoinPhysicsMaterial");
        ApplySettings();
    }
    
    private void ApplySettings()
    {
        if (physicsMaterial == null) return;
        
        physicsMaterial.dynamicFriction = dynamicFriction;
        physicsMaterial.staticFriction = staticFriction;
        physicsMaterial.bounciness = bounciness;
        physicsMaterial.frictionCombine = frictionCombine;
        physicsMaterial.bounceCombine = bounceCombine;
    }
    
    private void OnValidate()
    {
        if (physicsMaterial != null)
        {
            ApplySettings();
        }
    }
}