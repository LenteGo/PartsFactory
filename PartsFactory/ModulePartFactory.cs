using System;
using System.Collections.Generic;
using Experience.Effects;
using UnityEngine;

namespace PartsFactory
{
    /**
     * TODO:
     * thermal control after time warp
     * i18n
     * improve resources after warp
     * quantity
     * variants Support
     * category production multiplier
     * parts list
     */
    public class ModulePartFactory : PartModule, IResourceConsumer, IOverheatDisplay
    {
        private const string NominalStatus = "Nominal";
        private const string OverheatStatus = "Overheated";
        private const string ProductionSingle = "Single Mode";
        private const string ProductionCycle = "Cycle Mode";
        private const string StopTitle = "Stop Producting";
        private const string StartTitle = "Start Producting";
        private const string GroupName = "PrinterUiGroup";
        private const bool ShowDebug = false;

        private List<ModuleInventoryPart> _inventoryPartsCache;

        private readonly List<BaseEvent> _productionEvents = new List<BaseEvent>();
        private List<PartResourceDefinition> _consumedResources;
        public double inputReceived = 1d;
        private bool _lackingResources;
        private IResourceBroker _resBroker;

        List<string> partGroupsIdList = new List<String>();
        private bool _initializationDone;

        [KSPField(guiActive = true,
            guiName = "#autoLOC_475347")]
        public string displayStatus = string.Empty;

        [SerializeField] private List<ResourceRatio> resourcesList = new List<ResourceRatio>();

        [SerializeField] private List<String> predefinedParts = new List<string>();

        [KSPField(isPersistant = true)] public FloatCurve thermalEfficiency;

        public PartBlueprint currentBlueprint;

        [KSPField(guiActive = ShowDebug,
            isPersistant = true,
            guiName = "isOnProduction")]
        public bool isOnProduction;

        [KSPField(isPersistant = true)] public string status = NominalStatus;

        [KSPField(guiActive = ShowDebug,
            isPersistant = true,
            guiName = "materialProduced")]
        public float materialProduced;

        [KSPField(guiActive = ShowDebug,
            isPersistant = true,
            guiName = "materialRequired")]
        public float materialRequired;

        [KSPField] public int crewsRequired;

        [KSPField(isPersistant = true)] public float overheatTemperature = 500;

        [KSPField(isPersistant = true)] public float optimalTemperature = 370f;

        [KSPField(isPersistant = true)] public float heatProduction = 370f;

        [KSPField(guiActive = ShowDebug,
            isPersistant = true,
            guiName = "Last Update Time")]
        public double lastUpdateTime;

        [KSPField(isPersistant = true)] public bool isCycleProduction;

        [KSPEvent(guiActive = true,
            guiActiveEditor = false,
            guiName = ProductionSingle)]
        public void ToggleCycle()
        {
            isCycleProduction = !isCycleProduction;
            UpdateUiElements();
        }

        [KSPEvent(guiActive = false,
            category = GroupName,
            guiActiveEditor = false,
            guiName = StopTitle)]
        public void StopPartProductionEvent()
        {
            SetProductionState(false);
        }

        [KSPEvent(guiActive = true,
            category = GroupName,
            guiActiveEditor = false,
            guiName = StopTitle)]
        public void StartPartProductionEvent()
        {
            StartNewProduction(currentBlueprint);
        }

        protected virtual IResourceBroker ResBroker
        {
            get
            {
                IResourceBroker resBroker = this._resBroker;
                if (resBroker != null)
                    return resBroker;
                return this._resBroker = new ResourceBroker();
            }
        }

        private double GetEfficiencyMultiplier() => this.GetHeatThrottle();

        public void StartNewProduction(PartBlueprint blueprint)
        {
            if ((double) this.part.GetCrewCountOfExperienceEffect<RepairSkill>() < (double) this.crewsRequired)
            {
                this.status = "No engineers";
                SetProductionState(false);
                ScreenMessages.PostScreenMessage("Require engineers with repair skills");
                return;
            }

            if (currentBlueprint == null || !currentBlueprint.blueprintName.Equals(blueprint.blueprintName))
            {
                currentBlueprint = blueprint;
                materialProduced = 0;
                materialRequired = blueprint.mass * blueprint.quantity * 1000;
            }

            ModuleInventoryPart storageModule = GETStorageIfPossible(blueprint);
            if (!ReferenceEquals(storageModule, null))
            {
                status = NominalStatus;
                SetProductionState(true);
            }
            else
            {
                // Debug.Log("No free storage");
                this.status = "No free storage";
                SetProductionState(false);
                ScreenMessages.PostScreenMessage("No free storage");
            }
        }

        private ModuleInventoryPart GETStorageIfPossible(PartBlueprint blueprint)
        {
            // ModuleInventoryPart inventoryModule = this.part.FindModuleImplementing<ModuleInventoryPart>();
            ModuleInventoryPart biggestStorage = null;
            int maxCapacity = 0;
            if (_inventoryPartsCache == null)
                UpdatePartsCache(this.vessel);
            foreach (ModuleInventoryPart inventory in _inventoryPartsCache)
            {
                float requiredDStorageVolume = inventory.volumeCapacity + blueprint.volume * blueprint.quantity;
                // if (inventory.ContainsPart(scannedPartName))
                if (requiredDStorageVolume <= inventory.packedVolumeLimit && inventory.TotalEmptySlots() > 0 &&
                    maxCapacity < inventory.InventorySlots)
                {
                    maxCapacity = inventory.InventorySlots;
                    biggestStorage = inventory;
                }
            }

            return biggestStorage;
        }

        private void SetProductionState(bool state)
        {
            isOnProduction = state;
            UpdateUiElements();
        }

        private int savePartOnStorage(PartBlueprint blueprint)
        {
            int itemsCreated = 0;
            for (int itemsCount = 1; itemsCount <= materialProduced / materialRequired; itemsCount++)
            {
                ModuleInventoryPart storageModule = GETStorageIfPossible(blueprint);

                if (!ReferenceEquals(storageModule, null))
                {
                    int slotIndex = storageModule.FirstEmptySlot();

                    ProtoPartSnapshot protoPart = blueprint.GETProtoPart();
                    foreach (var resource in protoPart.resources)
                    {
                        if (!resource.resourceName.Equals("ElectricCharge") &&
                            !resource.resourceName.Equals("SolidFuel"))
                        {
                            resource.amount = 0;
                        }
                    }

                    storageModule.StoreCargoPartAtSlot(protoPart, slotIndex);
                    storageModule.storedParts[slotIndex].quantity = blueprint.quantity;

                    itemsCreated++;
                    ((UI_Grid) storageModule.Fields["InventorySlots"].uiControlEditor).updateSlotItems = true;
                    GameEvents.onModuleInventorySlotChanged.Fire(storageModule, slotIndex);
                    GameEvents.onModuleInventoryChanged.Fire(storageModule);
                }
                else
                {
                    this.status = "No free storage";
                    SetProductionState(false);
                    ScreenMessages.PostScreenMessage("No free storage!");
                    break;
                }

                if (!isCycleProduction)
                {
                    materialProduced = 0;
                    SetProductionState(false);
                    break;
                }
            }

            materialProduced %= materialRequired;
            return itemsCreated;
        }

        public override void OnActive()
        {
            base.OnActive();
            UpdatePartsCache(this.vessel);
            Debug.Log("gaga OnActive");
        }

        public override void OnInitialize()
        {
            base.OnInitialize();
            Debug.Log("gaga OnInitialize");
        }


        public override void OnAwake()
        {
            Debug.Log("gaga OnAwake");
            base.OnAwake();

            if (this.thermalEfficiency == null)
            {
                this.thermalEfficiency = new FloatCurve();
                this.thermalEfficiency.Add(0.0f, 1f);
            }

            this._consumedResources = _consumedResources ?? new List<PartResourceDefinition>();
        }

        private float GETPrintedPercents()
        {
            return materialRequired > 1 ? 100 * materialProduced / materialRequired : 0;
        }

        public void SetBlueprint(PartBlueprint blueprint)
        {
            if (currentBlueprint == null) return;
            ProtoPartSnapshot protoPart = blueprint.GETProtoPart();
            foreach (var resource in protoPart.resources)
            {
                resource.amount = 0;
            }

            protoPart.mass = 0;
        }

        private void UpdateUiElements()
        {
            if ((UnityEngine.Object) part.PartActionWindow != (UnityEngine.Object) null &&
                part.PartActionWindow.parameterGroups != null)
            {
                foreach (String groupId in partGroupsIdList)
                {
                    if (part.PartActionWindow.parameterGroups.TryGetValue(groupId, out var windowActionGroup))
                    {
                        windowActionGroup.gameObject.SetActive(!isOnProduction);
                    }
                }
            }

            this.Events["ToggleCycle"].guiName = isCycleProduction ? ProductionCycle : ProductionSingle;

            if (currentBlueprint != null)
            {
                BaseEvent stopEvent = this.Events["StopPartProductionEvent"];
                BaseEvent startEvent = this.Events["StartPartProductionEvent"];

                stopEvent.guiActive = isOnProduction;
                startEvent.guiActive = !isOnProduction;
                if (isOnProduction)
                {
                    stopEvent.guiName = StopTitle + " " + currentBlueprint.displayName;
                }
                else
                {
                    startEvent.guiName = StartTitle + " " + currentBlueprint.displayName;
                }
            }


            foreach (BaseEvent kspEvent in _productionEvents)
            {
                kspEvent.guiActive = !isOnProduction;
            }

            UpdateUiTexts();
        }

        public void UpdateUiTexts()
        {
            if (this._lackingResources)
            {
                this.displayStatus = "No enough resources";
            }
            else
            {
                this.displayStatus =
                    this.isOnProduction ? "Printed " + (int) GETPrintedPercents() + "%" : status;
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            SetBlueprint(currentBlueprint);
            this.resHandler.inputResources.Clear();
            foreach (ResourceRatio ratio in resourcesList)
            {
                this.resHandler.inputResources.Add(new ModuleResource()
                {
                    name = ratio.ResourceName,
                    title = KSPUtil.PrintModuleName(ratio.ResourceName),
                    id = ratio.ResourceName.GetHashCode(),
                    rate = ratio.Ratio
                });
            }

            foreach (String predefinedPartId in predefinedParts)
            {
                AvailablePart partInstance = PartLoader.getPartInfoByName(predefinedPartId);
                if (partInstance != null && ResearchAndDevelopment.PartTechAvailable(partInstance))
                {
                    String groupId = partInstance.category.ToString().Trim().ToLower().Replace(" ", "_");
                    partGroupsIdList.Add(groupId);
                    KSPEvent productEventUi = new KSPEvent()
                    {
                        guiActive = true,
                        groupName = groupId,
                        groupDisplayName = partInstance.category.displayDescription(),
                        guiActiveEditor = false,
                        groupStartCollapsed = true,
                        guiName = partInstance.title,
                    };

                    BaseEvent productionEvent = new BaseEvent(this.Events, productEventUi.name, () =>
                    {
                        PartBlueprint blueprint = new PartBlueprint(partInstance.name, "", 1);
                        currentBlueprint = blueprint;
                        SetBlueprint(currentBlueprint);
                        UpdateUiElements();
                    }, productEventUi);
                    this.Events.Add(productionEvent);
                    _productionEvents.Add(productionEvent);
                }
            }
        }

        public override void OnStartFinished(StartState state)
        {
            Debug.Log("gaga OnStartFinished");
            base.OnStartFinished(state);
            if (isOnProduction && Planetarium.GetUniversalTime() > lastUpdateTime)
            {
                RestoreTimeWarp();
            }

            if (HighLogic.LoadedSceneIsFlight)
            {
                GameEvents.onVesselPartCountChanged.Add(UpdatePartsCache);
                GameEvents.onVesselPartCountChanged.Fire(this.vessel);
            }

            this._initializationDone = true;
            UpdateUiElements();
        }

        public void UpdatePartsCache(Vessel eventVessel)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (_inventoryPartsCache == null)
                {
                    _inventoryPartsCache = new List<ModuleInventoryPart>();
                }
                else
                {
                    _inventoryPartsCache.Clear();
                }

                List<ModuleInventoryPart> allInventoryParts =
                    this.vessel.FindPartModulesImplementing<ModuleInventoryPart>();
                foreach (ModuleInventoryPart inventoryPart in allInventoryParts)
                {
                    if (!inventoryPart.part.HasModuleImplementing<ModulePartFactory>())
                        _inventoryPartsCache.Add(inventoryPart);
                }
            }
        }

        private void RestoreTimeWarp()
        {
            double efficiency = GetEfficiencyMultiplier();
            double deltaMaterial = Math.Min((Planetarium.GetUniversalTime() - lastUpdateTime) * efficiency,
                ResourceUtilities.GetMaxDeltaTime());

            foreach (ResourceRatio ratio in resourcesList)
            {
                PartResourceDefinition definition = PartResourceLibrary.Instance.GetDefinition(ratio.ResourceName);
                double resourceDelta = this.ResBroker.AmountAvailable(this.part, definition.id, deltaMaterial,
                    definition.resourceFlowMode);
                double fixedResourceDelta = deltaMaterial * (resourceDelta / (ratio.Ratio * deltaMaterial));
                deltaMaterial = Math.Min(deltaMaterial, fixedResourceDelta);
            }

            float oldMaterialValue = materialProduced;
            materialProduced += (float) deltaMaterial;
            int itemsProduced = savePartOnStorage(currentBlueprint);
            double resourcesAmount = itemsProduced * materialRequired - oldMaterialValue + this.materialProduced;
            double efficiencyMultiplier = GetEfficiencyMultiplier();

            foreach (ResourceRatio ratio in resourcesList)
            {
                PartResourceDefinition definition = PartResourceLibrary.Instance.GetDefinition(ratio.ResourceName);
                double used = this.ResBroker.RequestResource(this.part, definition.id,
                    resourcesAmount * ratio.Ratio * efficiencyMultiplier, deltaMaterial,
                    ResourceFlowMode.ALL_VESSEL);
                Debug.Log("Factory resources used: " + used);
            }

            if (isCycleProduction)
                StartNewProduction(currentBlueprint);
        }

        public void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && isOnProduction)
            {
                if (!_initializationDone)
                {
                    return;
                }

                if (isOnProduction && materialProduced > materialRequired)
                {
                    savePartOnStorage(currentBlueprint);
                    if (isCycleProduction)
                        StartNewProduction(currentBlueprint);
                    else
                        SetProductionState(false);
                }

                int count = this.resHandler.inputResources.Count;
                if (count > 0)
                {
                    double efficiencyMultiplier = GetEfficiencyMultiplier();
                    this.inputReceived =
                        this.resHandler.UpdateModuleResourceInputs(ref status, efficiencyMultiplier, 0.9, false, false,
                            false);

                    this._lackingResources = false;
                    int index = count;
                    while (index-- > 0)
                    {
                        ModuleResource currentResource = this.resHandler.inputResources[index];
                        if (!currentResource.available)
                        {
                            this._lackingResources = true;
                        }
                    }

                    float deltaMaterial = (float) this.inputReceived * TimeWarp.fixedDeltaTime;
                    materialProduced += deltaMaterial;
                    lastUpdateTime = Planetarium.GetUniversalTime();

                    if (this.part.temperature < optimalTemperature)
                        this.part.AddThermalFlux(heatProduction * Math.Pow(2 - GetHeatThrottle(), 2));
                    else
                        this.part.AddThermalFlux(heatProduction * Math.Pow(GetHeatThrottle() - 0.25, 2));
                    if (IsOverheating())
                    {
                        this.status = OverheatStatus;
                    }

                    if (this.status != NominalStatus || this._lackingResources)
                    {
                        ScreenMessages.PostScreenMessage(this.part.partName + " Production stopped. " + this.status);
                        SetProductionState(false);
                    }
                }
            }
        }

        public void LateUpdate()
        {
            if (isOnProduction)
                UpdateUiTexts();
        }

        // Resources interface 
        public List<PartResourceDefinition> GetConsumedResources() => this._consumedResources;

        // Heat methods
        public float GetHeatThrottle()
        {
            if (isOnProduction)
            {
                return this.thermalEfficiency.Evaluate((float) this.GetCoreTemperature());
            }

            return 1f;
        }

        public bool ModuleIsActive() => this.isOnProduction;

        public bool IsOverheating()
        {
            return this.part.temperature > this.overheatTemperature;
        }

        public double GetCoreTemperature()
        {
            return this.part.temperature;
        }

        public double GetGoalTemperature()
        {
            return this.optimalTemperature;
        }

        public virtual bool DisplayCoreHeat()
        {
            return true;
        }


        // ////////////////////
        // Config Node methods
        // ////////////////////

        public override void OnLoad(ConfigNode node)
        {
            Debug.Log("gaga OnLoad");
            base.OnLoad(node);
            LoadResources(node);
            LoadDefaultParts(node);
            currentBlueprint = LoadBlueprint(node, "currentBlueprint");
        }

        public override void OnSave(ConfigNode node)
        {
            Debug.Log("gaga OnSave");
            SaveResources(node);
            SaveDefaultParts(node);
            SaveBlueprint(node, "currentBlueprint", currentBlueprint);

            base.OnSave(node);
        }

        private void SaveBlueprint(ConfigNode node, string nodeName, PartBlueprint blueprint)
        {
            if (blueprint != null)
            {
                ConfigNode blueprintNode = new ConfigNode {name = nodeName};
                blueprint.Save(blueprintNode);
                node.AddNode(blueprintNode);
            }
        }

        private PartBlueprint LoadBlueprint(ConfigNode node, string nodeName)
        {
            if (node != null)
            {
                ConfigNode blueprintNode = null;
                if (node.TryGetNode(nodeName, ref blueprintNode))
                {
                    PartBlueprint blueprint = new PartBlueprint(blueprintNode);
                    return blueprint;
                }
            }

            return null;
        }


        private void SaveResources(ConfigNode node)
        {
            ConfigNode resourcesNodeList = new ConfigNode {name = "RESOURCES"};
            for (int index = 0; index < this.resourcesList.Count; ++index)
            {
                ConfigNode resourceNode = new ConfigNode {name = "RESOURCE"};
                resourcesList[index].Save(resourceNode);
                resourcesNodeList.AddNode(resourceNode);
            }

            node.AddNode(resourcesNodeList);
        }

        private void LoadResources(ConfigNode node)
        {
            ConfigNode resourcesNode = null;
            resourcesList.Clear();
            if (node.TryGetNode("RESOURCES", ref resourcesNode))
            {
                ConfigNode[] resources = resourcesNode.GetNodes("RESOURCE");
                foreach (ConfigNode resNode in resources)
                    if (resources.Length != 0)
                    {
                        ResourceRatio ratio = new ResourceRatio();
                        ratio.Load(resNode);
                        resourcesList.Add(ratio);
                    }
            }
        }

        private void SaveDefaultParts(ConfigNode moduleNode)
        {
            ConfigNode configNode = new ConfigNode {name = "DEFAULTS_PARTS"};
            foreach (String predefinedPartId in predefinedParts)
            {
                configNode.AddValue("Part", predefinedPartId);
            }

            moduleNode.AddNode(configNode);
        }

        private void LoadDefaultParts(ConfigNode node)
        {
            ConfigNode defaultPartsNode = null;
            if (node.TryGetNode("DEFAULTS_PARTS", ref defaultPartsNode))
            {
                predefinedParts = defaultPartsNode.GetValuesList("Part");
            }
        }
    }
}