var slot = jamm_item_find("tool", "submarine:bow-winch");
if (slot >= 0 && instance_exists(ToolData) && slot < array_length_1d(ToolData.unlocked) && ToolData.unlocked[slot] > 0)
    active = 1;
else
    active = 0;
