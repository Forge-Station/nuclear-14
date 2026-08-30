// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 Roudenn <romabond091@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Goobstation.Client.ItemSlotRenderer;

[RegisterComponent]
public sealed partial class ItemSlotRendererComponent : Component
{
    // [slotId] = layer mapping (in string form)
    [DataField("mapping")]
    public Dictionary<string, string> PrototypeLayerMappings = new();

    // [layerKey] = slotId
    [ViewVariables(VVAccess.ReadWrite)]
    public List<(object, string)> LayerMappings = new();

    [DataField]
    public bool ErrorOnMissing = true;

    // Number of extra sprite layers this renderer has appended (for multi-layer items such
    // as soups/pies). Tracked so they can be removed precisely without touching other layers.
    [ViewVariables]
    public int ExtraLayers;
}
