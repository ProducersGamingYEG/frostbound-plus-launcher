-- Vanilla's stock resolution menu has 32 entries. Keep only this menu bounded.
-- Re-enumerate on each menu initialization; never change a CVar or display mode here.
local modes = {}
local originalIDs = {}
local selectedResolution = nil
local count = 0
local originalSave = OptionsFrame_Save
local originalSelectedID = UIDropDownMenu_GetSelectedID

local function BuildModes(preferred)
    local all = { GetScreenResolutions() }
    count = table.getn(all)
    local current = GetCurrentResolution()
    local desired = preferred or all[current or 0]
    local desiredID = nil
    for i = 1, count do
        if all[i] == desired then desiredID = i; break end
    end
    if not desiredID then
        desiredID = current
        desired = all[current or 0]
    end
    modes = {}
    originalIDs = {}
    local limit = UIDROPDOWNMENU_MAXBUTTONS or 32
    local first = math.max(count - limit + 1, 1)
    if desiredID and desiredID > 0 and desiredID < first then
        table.insert(modes, all[desiredID])
        table.insert(originalIDs, desiredID)
        first = first + 1
    end
    local selected = nil
    for id = first, count do
        table.insert(modes, all[id])
        table.insert(originalIDs, id)
    end
    for id = 1, table.getn(modes) do
        if modes[id] == desired then selected = id; break end
    end
    selectedResolution = desired
    return selected
end

function OptionsFrameResolutionDropDown_Initialize()
    local selected = BuildModes(selectedResolution)
    OptionsFrameResolutionDropDown_LoadResolutions(unpack(modes))
    if selected then
        UIDropDownMenu_SetSelectedID(OptionsFrameResolutionDropDown, selected, 1)
    else
        OptionsFrameResolutionDropDown.selectedID = nil
        OptionsFrameResolutionDropDown.selectedValue = nil
        OptionsFrameResolutionDropDown.selectedName = nil
    end
end

-- The stock XML invokes this on BOTH OnLoad and OnShow. Translate every time.
function OptionsFrameResolutionDropDown_OnLoad()
    selectedResolution = nil
    UIDropDownMenu_Initialize(OptionsFrameResolutionDropDown,
        OptionsFrameResolutionDropDown_Initialize)
    UIDropDownMenu_SetWidth(90, OptionsFrameResolutionDropDown)
end

function OptionsFrameResolutionButton_OnClick()
    local id = this:GetID()
    selectedResolution = modes[id]
    UIDropDownMenu_SetSelectedID(OptionsFrameResolutionDropDown, id, 1)
end

function OptionsFrame_Save()
    -- Resolve the selected mode by its name against FRESH driver IDs. The driver
    -- list may have changed since the dialog opened (e.g. windowed/fullscreen).
    local selected = originalSelectedID(OptionsFrameResolutionDropDown)
    local desired = modes[selected or 0] or selectedResolution
    local function ResolveDriverID()
        local all = { GetScreenResolutions() }
        for id = 1, table.getn(all) do
            if all[id] == desired then return id end
        end
        local current = GetCurrentResolution()
        if current and all[current] then return current end
    end
    if not ResolveDriverID() then return end
    -- Translation is scoped to this save operation, independent of global 'this'.
    -- Restore even when native save raises an error. Other dropdowns are untouched.
    local getter = UIDropDownMenu_GetSelectedID
    UIDropDownMenu_GetSelectedID = function(frame)
        if frame == OptionsFrameResolutionDropDown then return ResolveDriverID() end
        return getter(frame)
    end
    local results = { pcall(originalSave) }
    UIDropDownMenu_GetSelectedID = getter
    if not results[1] then error(results[2]) end
    table.remove(results, 1)
    return unpack(results)
end

if OptionsFrameResolutionDropDown then OptionsFrameResolutionDropDown_OnLoad() end

SLASH_FROSTBOUNDVIDEO1 = "/fbvideo"
SlashCmdList["FROSTBOUNDVIDEO"] = function()
    DEFAULT_CHAT_FRAME:AddMessage("Frostbound video: " .. count ..
        " driver modes; " .. table.getn(modes) .. " menu entries.")
end



