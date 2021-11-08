using System;
using UnityEngine;

[Serializable]
public class PartBlueprint
{
    public string blueprintName;
    public string displayName;
    public string variant;
    public string category;
    public int quantity = -1;
    public float volume;
    public float mass;

    public PartBlueprint(ConfigNode node)
    {
        this.Load(node);
    }

    public PartBlueprint(string blueprintName, string variant, int quantity)
    {
        AvailablePart partInstance = PartLoader.getPartInfoByName(blueprintName);
        ModuleCargoPart scannedPartCargoModule = partInstance.partPrefab.FindModuleImplementing<ModuleCargoPart>();

        this.blueprintName = blueprintName;
        this.displayName = partInstance.title;
        this.variant = variant;
        this.category = partInstance.category.ToString();
        Debug.Log("TechRequired " + blueprintName);
        Debug.Log("TechRequired done");
        this.quantity = quantity;
        this.volume = scannedPartCargoModule.packedVolume;
        this.mass = partInstance.partPrefab.mass;
    }

    public void Load(ConfigNode node)
    {
        this.blueprintName = node.GetValue("blueprintName");
        this.displayName = node.GetValue("displayName");
        this.variant = node.GetValue("variant");
        this.category = node.GetValue("category");
        this.quantity = node.HasValue("techCost") ? int.Parse(node.GetValue("quantity")) : 1;
        this.volume = node.HasValue("techCost") ? float.Parse(node.GetValue("volume")) : 1;
        this.mass = node.HasValue("techCost") ? float.Parse(node.GetValue("mass")) : 1;
    }


    public void Save(ConfigNode node)
    {
        node.AddValue("blueprintName", this.blueprintName);
        node.AddValue("displayName", this.displayName);
        node.AddValue("variant", this.variant);
        node.AddValue("category", this.category);
        node.AddValue("quantity", this.quantity);
        node.AddValue("volume", this.volume);
        node.AddValue("mass", this.mass);
    }

    public ProtoPartSnapshot GETProtoPart()
    {
        AvailablePart partInfoByName = PartLoader.getPartInfoByName(this.blueprintName);
        return new ProtoPartSnapshot(partInfoByName.partPrefab, (ProtoVessel) null)
        {
            moduleVariantName = this.variant
        };
    }
}