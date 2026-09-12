-- Original Vanilla 1.12 addon: no Retail APIs, executable patch, roll choices,
-- master-loot distribution, confirmation acceptance, or forced loot closing.
local events = CreateFrame("Frame", "FrostboundAutoLootEvents", UIParent)
local pending = nil
local checkbox = nil

local function Settings()
    if type(FrostboundAutoLootDB) ~= "table" then
        FrostboundAutoLootDB = { enabled = true }
    elseif FrostboundAutoLootDB.enabled == nil then
        FrostboundAutoLootDB.enabled = true
    end
    return FrostboundAutoLootDB
end

local function Available()
    return type(LootSlot) == "function" and type(GetNumLootItems) == "function"
        and type(LootSlotIsItem) == "function" and type(LootSlotIsCoin) == "function"
end

local function Print(message)
    if DEFAULT_CHAT_FRAME then DEFAULT_CHAT_FRAME:AddMessage("Auto Loot: " .. message) end
end

local function SetEnabled(value)
    Settings().enabled = not not value
    pending = nil
    if checkbox then checkbox:SetChecked(Settings().enabled) end
end

local function CreateOptions()
    if checkbox or not UIOptionsFrame or not UIOptionsFrameDefaults then return end
    checkbox = CreateFrame("CheckButton", "FrostboundAutoLootOptions", UIOptionsFrame,
        "UICheckButtonTemplate")
    checkbox:SetWidth(26)
    checkbox:SetHeight(26)
    checkbox:SetPoint("LEFT", UIOptionsFrameDefaults, "RIGHT", 18, 0)
    getglobal("FrostboundAutoLootOptionsText"):SetText("AUTO LOOT")
    checkbox:SetScript("OnShow", function()
        this:SetChecked(Settings().enabled)
        if Available() then this:Enable() else this:Disable() end
    end)
    checkbox:SetScript("OnClick", function()
        SetEnabled(this:GetChecked())
        PlaySound("igMainMenuOptionCheckBoxOn")
    end)
    checkbox:SetScript("OnEnter", function()
        GameTooltip:SetOwner(this, "ANCHOR_TOPRIGHT")
        GameTooltip:SetText("AUTO LOOT", 1, 0.82, 0)
        GameTooltip:AddLine("Automatically collect eligible loot when you open a corpse or container.", 1, 1, 1, true)
        GameTooltip:AddLine("Shift suspends this addon's automation. Vanilla's built-in Shift-click behavior is unchanged.", 1, 1, 1, true)
        GameTooltip:AddLine("Binding confirmations and group rolls still require your choice. Changes save immediately.", 1, 1, 1, true)
        if not Available() then GameTooltip:AddLine("This client does not expose the required loot functions.", 1, 0.2, 0.2, true) end
        GameTooltip:Show()
    end)
    checkbox:SetScript("OnLeave", function() GameTooltip:Hide() end)
    checkbox:SetChecked(Settings().enabled)
    checkbox:Show()
end

local function ReservedForGroup(quality)
    local grouped = (GetNumPartyMembers and GetNumPartyMembers() > 0)
        or (GetNumRaidMembers and GetNumRaidMembers() > 0)
    if not grouped then return false end
    local method = GetLootMethod and GetLootMethod()
    local threshold = GetLootThreshold and GetLootThreshold() or 2
    return method and method ~= "freeforall" and quality and quality >= threshold
end

events:RegisterEvent("VARIABLES_LOADED")
events:RegisterEvent("LOOT_OPENED")
events:RegisterEvent("LOOT_CLOSED")
events:RegisterEvent("LOOT_BIND_CONFIRM")
events:RegisterEvent("UI_ERROR_MESSAGE")
events:SetScript("OnEvent", function()
    if event == "VARIABLES_LOADED" then
        Settings()
        CreateOptions()
    elseif event == "LOOT_OPENED" then
        pending = nil
        if Settings().enabled and Available() and not IsShiftKeyDown() then
            pending = { nextSlot = math.min(GetNumLootItems(), 100), elapsed = 0, attempted = {} }
        end
    elseif event == "LOOT_CLOSED" or event == "LOOT_BIND_CONFIRM" or event == "UI_ERROR_MESSAGE" then
        -- A second LootSlot call on a Vanilla binding-confirmation slot can
        -- ACCEPT that prompt. Stop immediately; never retry a slot.
        pending = nil
    end
end)

events:SetScript("OnUpdate", function()
    if not pending then return end
    if not Settings().enabled or IsShiftKeyDown() then pending = nil return end
    pending.elapsed = pending.elapsed + arg1
    if pending.elapsed < 0.10 then return end
    pending.elapsed = 0
    local session = pending
    local slot = session.nextSlot
    if slot < 1 then pending = nil return end
    session.nextSlot = slot - 1
    if session.attempted[slot] then return end
    session.attempted[slot] = true
    if LootSlotIsCoin(slot) then
        LootSlot(slot)
    elseif LootSlotIsItem(slot) then
        local texture, name, quantity, quality = GetLootSlotInfo(slot)
        if name and not ReservedForGroup(quality) then LootSlot(slot) end
    end
end)

SLASH_FROSTBOUNDAUTOLOOT1 = "/fbloot"
SlashCmdList["FROSTBOUNDAUTOLOOT"] = function(command)
    command = string.lower(command or "")
    if command == "on" then SetEnabled(true)
    elseif command == "off" then SetEnabled(false)
    elseif command == "options" then ShowUIPanel(UIOptionsFrame) end
    Print((Settings().enabled and "ON" or "OFF") .. ". Interface Options: AUTO LOOT checkbox beside Defaults. /fbloot on | off | options")
end
