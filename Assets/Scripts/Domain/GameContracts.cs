using System;
using System.Collections.Generic;

namespace OrbitalFoundry
{
    public enum FactionId { Neutral = 0, Player = 1, Ai = 2 }
    public enum MatchMode { Factory = 0, Tactical = 1, Ended = 2 }
    public enum MatchOutcome { None = 0, AiConceded = 1, PlayerDefeat = 2 }
    public enum BuildingKind { Extractor = 0, Refinery = 1, Assembler = 2, SaleDepot = 3, Shipyard = 4, Headquarters = 5, Fort = 6, Storage = 7, SolarArray = 8, ConstructionZone = 9 }
    public enum TokenStatus { Moving = 0, Waiting = 1, Frozen = 2 }
    public enum AiGoal { None = 0, BuildIncome = 1, Expand = 2, Defend = 3, Raid = 4 }
    public enum AlertKind { Info = 0, Raid = 1, BlockedRoute = 2, SaveError = 3, Victory = 4, Defeat = 5, Territory = 6 }
    public enum UiTool { Select = 0, BuildExtractor = 1, BuildRefinery = 2, BuildAssembler = 3, BuildDepot = 4, DrawBelt = 5, MoveCraft = 6, Attack = 7, Capture = 8, BuildShipyard = 9, Dismantle = 10, BuildFort = 11, BuildStorage = 12, BuildPurifier = 13, BuildSolarArray = 14 }
    public enum CommandKind
    {
        PlaceBuilding = 0,
        PlaceBelt = 1,
        SelectRecipe = 2,
        Research = 3,           // buys the next level of research node contentId
        // 4 was UnlockTech, retired with the research web
        BuyCraft = 5,
        AssignDefender = 6,
        MoveWorld = 7,
        StartAttack = 8,
        TacticalMove = 9,
        TacticalAttack = 10,
        StartCapture = 11,
        EndTurn = 12,
        Retreat = 13,
        Dismantle = 14,
        ToggleDepotItem = 15
    }

    [Serializable]
    public sealed class RulesDefinition
    {
        public int tickMilliseconds;
        public int startingCurrency;
        public int playerSalesGoal;
        public int graceTicks;
        public int normalCaptureTicks;
        public int tacticalActionsPerSide;
        public int worldMoveTicksPerCell;
        public int beltTravelTicksPerSegment;
        public int beltCapacityPerSegment;
        public int beltCost;
        public int raidWarningTicks;
        public int raidIntervalTicks;
        public int autosaveEveryTicks;
        public int maxCatchUpTicksPerFrame;
        public int aiCandidateChecksPerTick;
        // AI behaviour switches. When both are off the AI only builds and runs its own factory.
        public bool aiExpands;
        public bool aiRaids;
        // The AI builds a shipyard and keeps a defender in every cell it holds, without leaving its territory.
        public bool aiDefends;
        // Normal ticks an owned non-HQ cell may stay without a Fort or stationed unit before it reverts to neutral.
        public int abandonTicks;
        // Ion is a faction-wide power stockpile, never carried on belts. Extractors on nodes of this item add
        // ionPerExtraction to the owner's stockpile each cycle; recipes, construction and craft launches spend it.
        public string ionItemId = "";
        public int ionPerExtraction;
        public int startingIon;
        // Advance Wars rule: a damaged craft's attack and reactive (counter) stats scale with its remaining HP.
        public bool combatScalesWithHealth;
    }

    [Serializable]
    public sealed class ItemDefinition
    {
        public string id = "";
        public string displayName = "";
        public string colorHex = "";
        public bool sellable;
        public int baseSaleValue;
    }

    [Serializable]
    public sealed class IngredientDefinition
    {
        public string itemId = "";
        public int quantity;
    }

    [Serializable]
    public sealed class RecipeDefinition
    {
        public string id = "";
        public BuildingKind buildingKind;
        public List<IngredientDefinition> inputs = new List<IngredientDefinition>();
        public string outputItemId = "";
        public int outputQuantity;
        public int craftTicks;
        public int ionCost;     // spent when a cycle completes; the machine holds until the stockpile covers it
    }

    [Serializable]
    public sealed class BuildingDefinition
    {
        public string id = "";
        public string displayName = "";
        public BuildingKind kind;
        public int cost;
        public int width;
        public int height;
        public int inventoryCapacity;
        public List<string> allowedRecipeIds = new List<string>();
        public string colorHex = "";
        public int ionCost;     // spent on placement alongside the credit cost
        // Power, in MW: what this building draws while it is working, and what it supplies while active.
        public int powerDraw;
        public int powerSupply;
        // Goods delivered by belt to finish construction. A building with materials is placed as a construction
        // site that works only once every material has arrived.
        public List<IngredientDefinition> materials = new List<IngredientDefinition>();
        // Goods taken from the owner's Storage buildings when placed (no belt delivery).
        public List<IngredientDefinition> goodsCost = new List<IngredientDefinition>();
        // Construction zones: the building they deploy into the neighbouring cell once complete.
        public string deploysBuildingId = "";
        // Anchors (Headquarters, Fort): how many Solar Arrays their cell can hold.
        public int arraySlots;
    }

    [Serializable]
    public sealed class CraftDefinition
    {
        public string id = "";
        public string displayName = "";
        public string colorHex = "";
        public bool airborne;
        public int cost;
        public int movement;
        public int range;
        public int attackDamage;
        public int rangedAttackDamage;
        public int airAttackDamage;
        public int defenceDamage;
        public int rangedDefenceDamage;
        public int airDefenceDamage;
        public int maxHealth;
        public int armour;
        public int defenderArmourBonus;
        public int defenderCounterBonus;
        // Factory goods a shipyard must receive by belt before it can launch this craft.
        public List<IngredientDefinition> components = new List<IngredientDefinition>();
        public int buildTicks;
        public int ionCost;     // spent at launch; a finished craft waits in the shipyard until the stockpile covers it
    }

    /// <summary>
    /// What one research level grants. Kinds: unlock_recipe and unlock_building (targetId names it); percentage
    /// bonuses machine_speed_percent (targetId a building id, or empty for every machine), ion_cost_percent (recipe Ion
    /// saved), extraction_speed_percent, ion_yield_percent and sale_price_percent. Bonuses add across owned levels.
    /// </summary>
    [Serializable]
    public sealed class ResearchEffect
    {
        public string kind = "";
        public string targetId = "";
        public int amount;
    }

    [Serializable]
    public sealed class ResearchLevelDefinition
    {
        public int creditCost;
        public int ionCost;
        public string description = "";
        public List<ResearchEffect> effects = new List<ResearchEffect>();
    }

    [Serializable]
    public sealed class ResearchPrerequisite
    {
        public string nodeId = "";
        public int minimumLevel = 1;
    }

    /// <summary>
    /// A node in the research web, bought one level at a time. x/y place it on the web (root at 0,0, rows grow
    /// downward). A node opens once every prerequisite reaches its minimum level.
    /// </summary>
    [Serializable]
    public sealed class ResearchNodeDefinition
    {
        public string id = "";
        public string displayName = "";
        public string branch = "";
        public int x;
        public int y;
        public List<ResearchPrerequisite> prerequisites = new List<ResearchPrerequisite>();
        public List<ResearchLevelDefinition> levels = new List<ResearchLevelDefinition>();
    }

    [Serializable]
    public sealed class ContentCatalog
    {
        public int version;
        public RulesDefinition rules = new RulesDefinition();
        public List<ItemDefinition> items = new List<ItemDefinition>();
        public List<RecipeDefinition> recipes = new List<RecipeDefinition>();
        public List<BuildingDefinition> buildings = new List<BuildingDefinition>();
        public List<CraftDefinition> crafts = new List<CraftDefinition>();
        public List<ResearchNodeDefinition> research = new List<ResearchNodeDefinition>();
    }

    [Serializable]
    public sealed class TilePoint
    {
        public int x;
        public int y;
    }

    [Serializable]
    public sealed class ResourceNodeDefinition
    {
        public int id;
        public int x;
        public int y;
        public string itemId = "";
        public int rateTicks;
    }

    [Serializable]
    public sealed class MapCellDefinition
    {
        public int id;
        public int mapX;
        public int mapY;
        public int gridWidth;
        public int gridHeight;
        public FactionId startingOwner;
        public FactionId headquartersFor;
        public List<TilePoint> blockedTiles = new List<TilePoint>();
        public List<ResourceNodeDefinition> nodes = new List<ResourceNodeDefinition>();
        public List<SceneryDefinition> scenery = new List<SceneryDefinition>();
    }

    /// <summary>
    /// A piece of cell scenery. Blocking pieces (moonlets, wrecks) stand on one of the cell's blocked tiles;
    /// the rest (nebulae, debris, dust) are flat decoration that buildings and belts sit on top of.
    /// </summary>
    [Serializable]
    public sealed class SceneryDefinition
    {
        public string kind = "";
        public int x;
        public int y;
        public int variant;
        public bool blocking;
    }

    [Serializable]
    public sealed class StartCraftDefinition
    {
        public FactionId faction;
        public string craftTypeId = "";
        public int cellId;
        public bool assignedDefender;
    }

    [Serializable]
    public sealed class MapDefinition
    {
        public string id = "";
        public string displayName = "";
        public List<MapCellDefinition> cells = new List<MapCellDefinition>();
        public List<StartCraftDefinition> startingCrafts = new List<StartCraftDefinition>();
    }

    [Serializable]
    public sealed class ItemStack
    {
        public string itemId = "";
        public int quantity;
    }

    [Serializable]
    public sealed class FactionState
    {
        public FactionId id;
        public int currency;
        public int ion;
        public int cumulativeSales;
        // Power this tick, in MW: what active solar supplies and what working machines draw. When demand exceeds
        // supply, every machine slows to supply / demand.
        public int powerSupply;
        public int powerDemand;
        public bool suppressed;
        public List<ResearchProgress> research = new List<ResearchProgress>();
    }

    [Serializable]
    public sealed class ResearchProgress
    {
        public string nodeId = "";
        public int level;
    }

    [Serializable]
    public sealed class CellState
    {
        public int id;
        public FactionId owner;
        public FactionId headquartersFor;
        public int defenderCraftId = -1;
        public int lastOwnerChangeTick;
        // Tick at which this owned cell lost its last Fort or stationed unit; -1 while held.
        public int unheldSinceTick = -1;
        // True while the owner has a working Headquarters or completed Fort here. Machines in the cell run only then.
        // Refreshed every factory tick.
        public bool factoryOnline;
    }

    [Serializable]
    public sealed class ResourceNodeState
    {
        public int id;
        public int cellId;
        public int x;
        public int y;
        public string itemId = "";
        public int rateTicks;
        public int ticksUntilOutput;
        public int extractorBuildingId = -1;
    }

    [Serializable]
    public sealed class BuildingState
    {
        public int id;
        public FactionId owner;
        public string typeId = "";
        public int cellId;
        public int x;
        public int y;
        public int nodeId = -1;
        public string selectedRecipeId = "";
        public int recipeProgressTicks;
        public int inactiveUntilTick = -1;
        public List<ItemStack> inventory = new List<ItemStack>();
        // Shipyards only: craft type currently being built (empty when idle). Progress uses recipeProgressTicks.
        public string orderedCraftTypeId = "";
        // Sale depots and storages: item IDs this building will not request (or sell). Empty means all items.
        public List<string> blockedItemIds = new List<string>();
        // Headquarters and Forts hold their cell and switch its machines on.
        public bool anchor;
        // A construction site: it only receives its materials until all have arrived.
        public bool underConstruction;
        // Fraction of a work step banked under low power; a step runs each time it reaches 1.
        public float powerCharge;
        // Construction zones: the neighbouring cell across the border they deploy into.
        public int targetCellId = -1;
    }

    [Serializable]
    public sealed class BeltSegmentState
    {
        public int id;
        public FactionId owner;
        public int fromCellId;
        public int fromX;
        public int fromY;
        public int toCellId;
        public int toX;
        public int toY;
        public int inactiveUntilTick = -1;
    }

    [Serializable]
    public sealed class ItemTokenState
    {
        public int id;
        public string itemId = "";
        public int sourceBuildingId;
        public int targetBuildingId;
        public List<int> routeSegmentIds = new List<int>();
        public int segmentIndex;
        public int ticksUntilNextSegment;
        public TokenStatus status;
    }

    [Serializable]
    public sealed class PullRequestState
    {
        public int id;
        public int targetBuildingId;
        public string itemId = "";
        public int quantity;
        public int createdTick;
    }

    [Serializable]
    public sealed class CraftState
    {
        public int id;
        public FactionId owner;
        public string typeId = "";
        public int cellId;
        public int currentHealth;
        public bool withdrawn;
        public int worldDestinationCellId = -1;
        public List<int> worldPathCellIds = new List<int>();
        public int worldPathIndex;
        public int worldTicksUntilNextCell;
    }

    [Serializable]
    public sealed class CaptureHoldState
    {
        public int cellId;
        public int craftId;
        public FactionId faction;
        public bool tactical;
        public int progressTicks;
        public int startedRound;
    }

    [Serializable]
    public sealed class EncounterState
    {
        public bool active;
        public int targetCellId = -1;
        public FactionId attacker;
        public FactionId turn;
        public int round;
        public int actionsRemaining;
        public int pendingDefenderCellId = -1;
        public FactionId pendingDefenderOwner;
        public List<int> battleCellIds = new List<int>();
        public List<int> participantCraftIds = new List<int>();
        public List<int> actedCraftIds = new List<int>();
    }

    [Serializable]
    public sealed class GameCommand
    {
        public CommandKind kind;
        public FactionId actor;
        public int craftId = -1;
        public int targetCraftId = -1;
        public int buildingId = -1;
        public int beltId = -1;
        public int fromCellId = -1;
        public int fromX;
        public int fromY;
        public int toCellId = -1;
        public int toX;
        public int toY;
        public int targetCellId = -1;
        public string contentId = "";
    }

    [Serializable]
    public sealed class AiState
    {
        public AiGoal goal;
        public string targetItemId = "";
        public int targetCellId = -1;
        public int nextPlanningTick;
        public int plannerCursor;
        public int nextRaidTick;
        public int raidCraftId = -1;
        public int raidTargetCellId = -1;
        public bool raidWarningShown;
        public List<GameCommand> queuedCommands = new List<GameCommand>();
    }

    [Serializable]
    public sealed class AlertState
    {
        public int id;
        public AlertKind kind;
        public string message = "";
        public int cellId = -1;
        public int createdTick;
        public bool dismissed;
    }

    [Serializable]
    public sealed class WorldState
    {
        public string mapId = "";
        public MatchMode mode;
        public MatchOutcome outcome;
        public int tick;
        public int nextId;
        public int randomSeed;
        public List<FactionState> factions = new List<FactionState>();
        public List<CellState> cells = new List<CellState>();
        public List<ResourceNodeState> nodes = new List<ResourceNodeState>();
        public List<BuildingState> buildings = new List<BuildingState>();
        public List<BeltSegmentState> belts = new List<BeltSegmentState>();
        public List<ItemTokenState> tokens = new List<ItemTokenState>();
        public List<PullRequestState> pullRequests = new List<PullRequestState>();
        public List<CraftState> crafts = new List<CraftState>();
        public List<CaptureHoldState> captureHolds = new List<CaptureHoldState>();
        public EncounterState encounter = new EncounterState();
        public AiState ai = new AiState();
        public List<AlertState> alerts = new List<AlertState>();
    }

    [Serializable]
    public sealed class SaveEnvelope
    {
        public int schemaVersion;
        public int catalogVersion;
        public long savedUtcTicks;
        public WorldState world = new WorldState();
    }

    public sealed class CommandResult
    {
        public bool success;
        public bool stateChanged;
        public string code = "";
        public string message = "";
    }

    public sealed class BeltPathResult
    {
        public bool success;
        public string reason = "";
        public List<int> segmentIds = new List<int>();
    }

    public sealed class BeltStep
    {
        public int fromCellId;
        public int fromX;
        public int fromY;
        public int toCellId;
        public int toX;
        public int toY;
    }

    public sealed class BeltPlanResult
    {
        public bool success;
        public string reason = "";
        public int totalCost;
        public List<BeltStep> steps = new List<BeltStep>();
    }

    public sealed class CombatPreview
    {
        public bool legal;
        public string reason = "";
        public int attackerCraftId;
        public int defenderCraftId;
        public string attackStatName = "";
        public int attackBase;
        public int attackDamageAfterArmour;
        public string counterStatName = "";
        public int counterBase;
        public int counterDamageAfterArmour;
        public bool defenderSurvives;
    }

    /// <summary>One resolved attack, as the battle scene replays it. Runtime only; never saved.</summary>
    public sealed class CombatEvent
    {
        public string attackerType = "", defenderType = "";
        public FactionId attackerOwner, defenderOwner;
        public int attackerMaxHealth, defenderMaxHealth;
        public int attackerHealthBefore, attackerHealthAfter;
        public int defenderHealthBefore, defenderHealthAfter;
        public bool defenderAssigned;
        public int damage, counter;
    }

    public sealed class FactoryPlanResult
    {
        public bool success;
        public string reason = "";
        public string productItemId = "";
        public int estimatedCost;
        public List<GameCommand> commands = new List<GameCommand>();
    }

    public sealed class UiState
    {
        public UiTool activeTool;
        public int selectedCellId = -1;
        public int selectedCraftId = -1;
        public int selectedBuildingId = -1;
        public int selectedNodeId = -1;
        public int beltFromCellId = -1;
        public int beltFromX;
        public int beltFromY;
        public bool pointerOverUi;
        public string statusMessage = "";
    }
}
