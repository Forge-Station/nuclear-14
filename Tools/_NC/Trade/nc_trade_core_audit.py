#!/usr/bin/env python3
"""
NC Trade Core audit.

Purpose:
- Keep Trade / NcStore / Contracts locked to the current post-5.8R/6.0 schema.
- Fail fast if legacy Exchange, old Supply fields, or old Retrieval route fields return.
- Require Retrieval route claim.mode to be explicit.

This script intentionally uses only the Python standard library. It is a structural
content audit, not a full YAML parser.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path
from typing import Iterable, NamedTuple


ROOT_MARKERS = ("Content.Shared", "Content.Server", "Resources")
TRADE_RESOURCE_DIR = Path("Resources/Prototypes/_NC/Trade")
TRADE_YAML_DIRS = (
    TRADE_RESOURCE_DIR,
    Path("Resources/Prototypes/Corvax/Trade/Contract"),
)


class Issue(NamedTuple):
    severity: str
    path: Path
    line: int
    message: str


def find_repo_root(start: Path) -> Path:
    cur = start.resolve()
    for candidate in (cur, *cur.parents):
        if all((candidate / marker).exists() for marker in ROOT_MARKERS):
            return candidate
    return cur


REPO_ROOT = find_repo_root(Path.cwd())


EXCLUDED_DIR_PARTS = {
    ".git",
    ".idea",
    ".vs",
    "bin",
    "obj",
    "TestResults",
}


TEXT_SUFFIXES = {".cs", ".yml", ".yaml"}


def should_skip(path: Path) -> bool:
    parts = set(path.parts)
    return bool(parts & EXCLUDED_DIR_PARTS)


def iter_files(base_dirs: Iterable[Path], suffixes: set[str]) -> Iterable[Path]:
    for base in base_dirs:
        abs_base = REPO_ROOT / base
        if not abs_base.exists():
            continue
        for path in abs_base.rglob("*"):
            if path.is_file() and path.suffix.lower() in suffixes and not should_skip(path):
                yield path


def rel(path: Path) -> Path:
    try:
        return path.relative_to(REPO_ROOT)
    except ValueError:
        return path


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig", errors="replace")


def line_no(text: str, index: int) -> int:
    return text.count("\n", 0, index) + 1


def indent_of(line: str) -> int:
    return len(line) - len(line.lstrip(" "))


def add_issue(issues: list[Issue], severity: str, path: Path, line: int, message: str) -> None:
    issues.append(Issue(severity, rel(path), line, message))


def audit_no_live_exchange(issues: list[Issue]) -> None:
    patterns = {
        "StoreMode.Exchange": "legacy StoreMode.Exchange must not return",
        "TryExchange": "legacy TryExchange path must not return",
        "HasExchange": "legacy HasExchange flag must not return",
        "hasExchange": "legacy hasExchange serialized flag must not return",
    }

    # Scope to NC Trade only. Other systems may legitimately contain similarly named mechanics
    # such as construction part exchangers.
    for path in iter_files([
        Path("Content.Shared/_NC/Trade"),
        Path("Content.Server/_NC/Trade"),
        Path("Content.Client/_NC/Trade"),
        TRADE_RESOURCE_DIR,
    ], TEXT_SUFFIXES):
        text = read_text(path)
        for pattern, message in patterns.items():
            idx = text.find(pattern)
            if idx >= 0:
                add_issue(issues, "P0", path, line_no(text, idx), message)


TYPE_RE = re.compile(r"^-\s*type:\s*([A-Za-z0-9_]+)\s*$")
KEY_RE = re.compile(r"^(?P<indent>\s*)(?P<key>[A-Za-z0-9_]+)\s*:(?P<value>.*)$")
HEX_COLOR_RE = re.compile(r'^"?#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})"?$')


class ProtoBlock(NamedTuple):
    type_name: str
    path: Path
    start_line: int
    lines: list[tuple[int, str]]


def split_proto_blocks(path: Path) -> list[ProtoBlock]:
    blocks: list[ProtoBlock] = []
    current_type: str | None = None
    current_start = 0
    current_lines: list[tuple[int, str]] = []

    for num, line in enumerate(read_text(path).splitlines(), start=1):
        match = TYPE_RE.match(line)
        if match:
            if current_type is not None:
                blocks.append(ProtoBlock(current_type, path, current_start, current_lines))
            current_type = match.group(1)
            current_start = num
            current_lines = [(num, line)]
            continue

        if current_type is not None:
            current_lines.append((num, line))

    if current_type is not None:
        blocks.append(ProtoBlock(current_type, path, current_start, current_lines))

    return blocks


def block_text(block: ProtoBlock) -> str:
    return "\n".join(line for _, line in block.lines)


def top_level_value(block: ProtoBlock, key: str) -> tuple[int, str] | None:
    for num, line in block.lines:
        match = KEY_RE.match(line)
        if not match:
            continue
        if indent_of(line) != 2:
            continue
        if match.group("key") != key:
            continue
        return num, match.group("value").strip()
    return None


def proto_id(block: ProtoBlock) -> str:
    value = top_level_value(block, "id")
    return value[1] if value else ""


def has_key(block: ProtoBlock, key: str) -> bool:
    rx = re.compile(rf"^\s*{re.escape(key)}\s*:", re.MULTILINE)
    return bool(rx.search(block_text(block)))


def has_key_value(block: ProtoBlock, key: str, value: str) -> bool:
    rx = re.compile(rf"^\s*{re.escape(key)}\s*:\s*{re.escape(value)}\s*$", re.MULTILINE)
    return bool(rx.search(block_text(block)))


def first_key_line(block: ProtoBlock, key: str) -> int:
    rx = re.compile(rf"^\s*{re.escape(key)}\s*:")
    for num, line in block.lines:
        if rx.match(line):
            return num
    return block.start_line


def audit_barter_category(block: ProtoBlock, issues: list[Issue]) -> None:
    if has_key(block, "entries"):
        add_issue(issues, "P1", block.path, first_key_line(block, "entries"), "ncBarterCategory inline entries are forbidden; use listings")


SUPPLY_LEGACY_KEYS = {
    "requirements",
    "require",
    "rewards",
    "guaranteed",
    "random",
    "money",
    "amount",
    "chance",
    "prob",
}


RETRIEVAL_LEGACY_KEYS = {
    "targets",
    "targetCount",
    "spawn",
    "requireSpawned",
    "givePinpointer",
    "pinpointerPrototype",
    "hint",
}


HUNT_LEGACY_KEYS = {
    "targetItem",
    "required",
    "match",
    "objectiveType",
    "runtime",
    "targets",
    "targetCount",
}


VALID_OFFER_ENTRY_TYPES = {
    "Supply": "ncSupplyContract",
    "Retrieval": "ncRetrievalContract",
    "Hunt": "ncHuntContract",
}


class OfferPoolEntry(NamedTuple):
    line: int
    type_name: str | None
    contract_id: str | None
    weight: int | None


class OfferGroupEntry(NamedTuple):
    line: int
    pool: str | None
    min_visible: int
    max_visible: int
    fill_weight: int | None


def parse_int(value: str) -> int | None:
    if re.fullmatch(r"-?\d+", value):
        return int(value)
    return None


def parse_offer_pool_entries(block: ProtoBlock) -> list[OfferPoolEntry]:
    entries: list[OfferPoolEntry] = []
    entries_indent: int | None = None
    current_line: int | None = None
    current_type: str | None = None
    current_id: str | None = None
    current_weight: int | None = None
    list_item_re = re.compile(r"^(?P<indent>\s*)-\s*(?P<rest>.*)$")

    def finalize() -> None:
        nonlocal current_line, current_type, current_id, current_weight
        if current_line is not None:
            entries.append(OfferPoolEntry(current_line, current_type, current_id, current_weight))
        current_line = None
        current_type = None
        current_id = None
        current_weight = None

    for num, line in block.lines:
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue

        match = KEY_RE.match(line)
        if match and match.group("key") == "entries" and indent_of(line) == 2:
            entries_indent = indent_of(line)
            continue

        if entries_indent is None:
            continue

        indent = indent_of(line)
        if indent <= entries_indent and num > block.start_line and not stripped.startswith("-"):
            finalize()
            break

        item_match = list_item_re.match(line)
        if item_match and indent >= entries_indent:
            finalize()
            current_line = num
            rest = item_match.group("rest").strip()
            if rest.startswith("type:"):
                current_type = rest.split(":", 1)[1].strip()
            elif rest.startswith("id:"):
                current_id = rest.split(":", 1)[1].strip()
            elif rest.startswith("weight:"):
                current_weight = parse_int(rest.split(":", 1)[1].strip())
            continue

        if current_line is None:
            continue

        key_match = KEY_RE.match(line)
        if not key_match:
            continue

        key = key_match.group("key")
        value = key_match.group("value").strip()
        if key == "type":
            current_type = value
        elif key == "id":
            current_id = value
        elif key == "weight":
            current_weight = parse_int(value)

    finalize()
    return entries


def parse_contract_offer_groups(block: ProtoBlock) -> list[OfferGroupEntry]:
    groups: list[OfferGroupEntry] = []
    offers_indent: int | None = None
    groups_indent: int | None = None
    current_line: int | None = None
    current_pool: str | None = None
    current_min = 0
    current_max = 1
    current_fill: int | None = None
    list_item_re = re.compile(r"^(?P<indent>\s*)-\s*(?P<rest>.*)$")

    def finalize() -> None:
        nonlocal current_line, current_pool, current_min, current_max, current_fill
        if current_line is not None:
            groups.append(OfferGroupEntry(current_line, current_pool, current_min, current_max, current_fill))
        current_line = None
        current_pool = None
        current_min = 0
        current_max = 1
        current_fill = None

    for num, line in block.lines:
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue

        indent = indent_of(line)
        if offers_indent is not None and groups_indent is not None:
            item_match = list_item_re.match(line)
            if item_match and indent >= groups_indent:
                finalize()
                current_line = num
                rest = item_match.group("rest").strip()
                if rest.startswith("pool:"):
                    current_pool = rest.split(":", 1)[1].strip()
                elif rest.startswith("minVisible:"):
                    current_min = parse_int(rest.split(":", 1)[1].strip()) or 0
                elif rest.startswith("maxVisible:"):
                    current_max = parse_int(rest.split(":", 1)[1].strip()) or 1
                elif rest.startswith("fillWeight:"):
                    current_fill = parse_int(rest.split(":", 1)[1].strip())
                continue

        match = KEY_RE.match(line)
        if not match:
            continue

        key = match.group("key")
        value = match.group("value").strip()

        if key == "contractOffers" and indent == 2:
            offers_indent = indent
            continue

        if offers_indent is None:
            continue

        if indent <= offers_indent and num > block.start_line:
            finalize()
            break

        if key == "groups" and indent > offers_indent:
            groups_indent = indent
            continue

        if groups_indent is None:
            continue

        if indent <= groups_indent and num > block.start_line and not stripped.startswith("-"):
            finalize()
            groups_indent = None
            continue

        if current_line is None:
            continue

        if key == "pool":
            current_pool = value
        elif key == "minVisible":
            parsed = parse_int(value)
            if parsed is not None:
                current_min = parsed
        elif key == "maxVisible":
            parsed = parse_int(value)
            if parsed is not None:
                current_max = parsed
        elif key == "fillWeight":
            current_fill = parse_int(value)

    finalize()
    return groups


def audit_supply_contract(block: ProtoBlock, issues: list[Issue]) -> None:
    for key in sorted(SUPPLY_LEGACY_KEYS):
        if has_key(block, key):
            add_issue(issues, "P1", block.path, first_key_line(block, key), f"legacy Supply field '{key}' is forbidden")

    if has_key(block, "difficulty"):
        add_issue(issues, "P1", block.path, first_key_line(block, "difficulty"), "ncSupplyContract difficulty is forbidden; use offer pool grouping/order/color")

    if not has_key(block, "targets"):
        add_issue(issues, "P1", block.path, block.start_line, "ncSupplyContract must define targets")
    if not has_key(block, "reward"):
        add_issue(issues, "P1", block.path, block.start_line, "ncSupplyContract must define reward")


def audit_retrieval_contract(block: ProtoBlock, issues: list[Issue]) -> None:
    for key in sorted(RETRIEVAL_LEGACY_KEYS):
        if has_key(block, key):
            add_issue(issues, "P1", block.path, first_key_line(block, key), f"legacy Retrieval field '{key}' is forbidden")

    if has_key(block, "difficulty"):
        add_issue(issues, "P1", block.path, first_key_line(block, "difficulty"), "ncRetrievalContract difficulty is forbidden; use offer pool grouping/order/color")

    if not has_key(block, "cargo"):
        add_issue(issues, "P1", block.path, block.start_line, "ncRetrievalContract must define cargo")
    if not has_key(block, "route"):
        add_issue(issues, "P1", block.path, block.start_line, "ncRetrievalContract must define route")
    if not has_key(block, "reward"):
        add_issue(issues, "P1", block.path, block.start_line, "ncRetrievalContract must define reward")


def audit_retrieval_route(block: ProtoBlock, issues: list[Issue]) -> None:
    lines = block.lines
    claim_indent: int | None = None
    claim_line: int | None = None
    claim_mode: str | None = None
    claim_proof_line: int | None = None
    top_level_proof_line: int | None = None

    for num, line in lines:
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue

        match = KEY_RE.match(line)
        if not match:
            continue

        key = match.group("key")
        value = match.group("value").strip()
        indent = indent_of(line)

        if key == "proof" and indent <= 2:
            top_level_proof_line = num

        if key == "claim":
            claim_indent = indent
            claim_line = num
            continue

        if claim_indent is not None and indent <= claim_indent and num > (claim_line or 0):
            claim_indent = None

        if claim_indent is not None and indent > claim_indent:
            if key == "mode":
                claim_mode = value
            elif key == "proof":
                claim_proof_line = num

        if key == "consumeCargo" and value.lower() == "false":
            add_issue(issues, "P1", block.path, num, "delivery.consumeCargo: false is forbidden until lockDeliveredCargo is implemented")
        if key == "lockDeliveredCargo" and value.lower() == "true":
            add_issue(issues, "P1", block.path, num, "delivery.lockDeliveredCargo: true is forbidden until locking is implemented")

        if key in RETRIEVAL_LEGACY_KEYS:
            add_issue(issues, "P1", block.path, num, f"legacy Retrieval route field '{key}' is forbidden")

    if top_level_proof_line is not None:
        add_issue(issues, "P1", block.path, top_level_proof_line, "top-level route proof is forbidden; use claim.proof")

    if claim_line is None:
        add_issue(issues, "P1", block.path, block.start_line, "ncRetrievalRoutePreset must define claim.mode")
        return

    if not claim_mode:
        add_issue(issues, "P1", block.path, claim_line, "ncRetrievalRoutePreset claim must define mode")
        return

    if claim_mode == "StoreCargo":
        if claim_proof_line is not None:
            add_issue(issues, "P1", block.path, claim_proof_line, "StoreCargo route must not define claim.proof")
    elif claim_mode == "DestinationProof":
        if claim_proof_line is None:
            add_issue(issues, "P1", block.path, claim_line, "DestinationProof route must define claim.proof")
    else:
        add_issue(issues, "P1", block.path, claim_line, f"unknown Retrieval claim.mode '{claim_mode}'")


def audit_reward_entries_have_type_and_count(
    block: ProtoBlock,
    issues: list[Issue],
    owner_kind: str,
    owner_id_line: int,
) -> None:
    reward_indent: int | None = None
    reward_line: int | None = None
    entry_line: int | None = None
    entry_has_type = False
    entry_has_count = False
    seen_entries = 0

    list_item_re = re.compile(r"^\s*-\s*(?P<rest>.*)$")

    def finalize_entry() -> None:
        nonlocal entry_line, entry_has_type, entry_has_count
        if entry_line is None:
            return
        if not entry_has_type:
            add_issue(issues, "P1", block.path, entry_line, f"{owner_kind} reward entry must define type")
        if not entry_has_count:
            add_issue(issues, "P1", block.path, entry_line, f"{owner_kind} reward entry must define count")
        entry_line = None
        entry_has_type = False
        entry_has_count = False

    for num, line in block.lines:
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue

        key_match = KEY_RE.match(line)
        if key_match:
            key = key_match.group("key")
            indent = indent_of(line)

            if key == "reward":
                reward_indent = indent
                reward_line = num
                finalize_entry()
                continue

            if reward_indent is not None and indent <= reward_indent and num > (reward_line or 0):
                finalize_entry()
                reward_indent = None
                reward_line = None

            if reward_indent is not None and indent > reward_indent and entry_line is not None and key == "count":
                entry_has_count = True

        if reward_indent is None or reward_line is None:
            continue

        if indent_of(line) < reward_indent or num <= reward_line:
            continue

        item_match = list_item_re.match(line)
        if not item_match:
            continue

        finalize_entry()
        seen_entries += 1
        entry_line = num
        rest = item_match.group("rest").strip()

        if rest.startswith("type:"):
            entry_has_type = True
        if rest.startswith("count:") or " count:" in rest:
            entry_has_count = True

    finalize_entry()

    if reward_line is not None and seen_entries == 0:
        add_issue(issues, "P1", block.path, owner_id_line, f"{owner_kind} must define at least one reward entry")


def audit_hunt_contract(block: ProtoBlock, issues: list[Issue]) -> None:
    for key in sorted(HUNT_LEGACY_KEYS):
        if has_key(block, key):
            add_issue(issues, "P1", block.path, first_key_line(block, key), f"legacy Hunt field '{key}' is forbidden")

    if has_key(block, "difficulty"):
        add_issue(issues, "P1", block.path, first_key_line(block, "difficulty"), "ncHuntContract difficulty is forbidden; use offer pool grouping/order/color")

    if not has_key(block, "target"):
        add_issue(issues, "P1", block.path, block.start_line, "ncHuntContract must define target")
    if not has_key(block, "completion"):
        add_issue(issues, "P1", block.path, block.start_line, "ncHuntContract must define completion")
    if not has_key(block, "reward"):
        add_issue(issues, "P1", block.path, block.start_line, "ncHuntContract must define reward")

    target_indent: int | None = None
    target_line: int | None = None
    target_group_line: int | None = None
    target_prototype_line: int | None = None
    target_count_line: int | None = None
    target_count_value: str | None = None

    completion_indent: int | None = None
    completion_line: int | None = None
    completion_mode: str | None = None
    completion_trophy_line: int | None = None

    for num, line in block.lines:
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue

        match = KEY_RE.match(line)
        if not match:
            continue

        key = match.group("key")
        value = match.group("value").strip()
        indent = indent_of(line)

        if key == "target":
            target_indent = indent
            target_line = num
            continue

        if target_indent is not None and indent <= target_indent and num > (target_line or 0):
            target_indent = None

        if target_indent is not None and indent > target_indent:
            if key == "group":
                target_group_line = num
            elif key == "prototype":
                target_prototype_line = num
            elif key == "count":
                target_count_line = num
                target_count_value = value

        if key == "completion":
            completion_indent = indent
            completion_line = num
            continue

        if completion_indent is not None and indent <= completion_indent and num > (completion_line or 0):
            completion_indent = None

        if completion_indent is not None and indent > completion_indent:
            if key == "mode":
                completion_mode = value
            elif key == "trophy":
                completion_trophy_line = num

    if target_line is not None:
        has_group = target_group_line is not None
        has_prototype = target_prototype_line is not None
        if has_group == has_prototype:
            add_issue(
                issues,
                "P1",
                block.path,
                target_line,
                "ncHuntContract target must define exactly one of group/prototype",
            )

        if target_count_line is None:
            add_issue(issues, "P1", block.path, target_line, "ncHuntContract target must define count")
        elif target_count_value is not None and re.fullmatch(r"-?\d+", target_count_value):
            if int(target_count_value) <= 0:
                add_issue(issues, "P1", block.path, target_count_line, "ncHuntContract target count must be > 0")

    if completion_line is not None:
        if not completion_mode:
            add_issue(issues, "P1", block.path, completion_line, "ncHuntContract completion must define mode")
        elif completion_mode == "TrophyTurnIn":
            if completion_trophy_line is None:
                add_issue(issues, "P1", block.path, completion_line, "TrophyTurnIn completion must define trophy")
        elif completion_mode == "ConfirmedKill":
            pass
        else:
            add_issue(issues, "P1", block.path, completion_line, f"unknown Hunt completion.mode '{completion_mode}'")

    audit_reward_entries_have_type_and_count(block, issues, "ncHuntContract", block.start_line)


def audit_contract_offer_pool(
    block: ProtoBlock,
    issues: list[Issue],
    contract_ids_by_type: dict[str, set[str]],
) -> None:
    pool_id = proto_id(block) or "<unknown>"
    if not has_key(block, "name"):
        add_issue(issues, "P1", block.path, block.start_line, "ncContractOfferPool must define name")
    color = top_level_value(block, "color")
    if color is None:
        add_issue(issues, "P1", block.path, block.start_line, "ncContractOfferPool must define color for UI grouping")
    elif not HEX_COLOR_RE.fullmatch(color[1]):
        add_issue(issues, "P1", block.path, color[0], "ncContractOfferPool color must be a hex string like \"#C9A45A\"")
    if not has_key(block, "entries"):
        add_issue(issues, "P1", block.path, block.start_line, "ncContractOfferPool must define entries")
        return

    entries = parse_offer_pool_entries(block)
    if not entries:
        add_issue(issues, "P1", block.path, block.start_line, "ncContractOfferPool.entries must not be empty")
        return

    seen_ids: set[str] = set()
    for entry in entries:
        if not entry.type_name:
            add_issue(issues, "P1", block.path, entry.line, "ncContractOfferPool entry must define type")
            continue

        if entry.type_name not in VALID_OFFER_ENTRY_TYPES:
            add_issue(
                issues,
                "P1",
                block.path,
                entry.line,
                f"ncContractOfferPool entry type '{entry.type_name}' must be Supply, Retrieval, or Hunt",
            )
            continue

        if not entry.contract_id:
            add_issue(issues, "P1", block.path, entry.line, "ncContractOfferPool entry must define id")
            continue

        if entry.contract_id in seen_ids:
            add_issue(
                issues,
                "P1",
                block.path,
                entry.line,
                f"duplicate contract id '{entry.contract_id}' inside offer pool '{pool_id}' is forbidden",
            )
        seen_ids.add(entry.contract_id)

        target_type = VALID_OFFER_ENTRY_TYPES[entry.type_name]
        if entry.contract_id not in contract_ids_by_type.get(target_type, set()):
            add_issue(
                issues,
                "P1",
                block.path,
                entry.line,
                f"offer pool '{pool_id}' references missing {target_type} '{entry.contract_id}'",
            )

        if entry.weight is not None and entry.weight <= 0:
            add_issue(issues, "P1", block.path, entry.line, "ncContractOfferPool entry weight must be > 0")


def audit_contract_offers(
    block: ProtoBlock,
    issues: list[Issue],
    offer_pool_ids: set[str],
) -> None:
    for key in ("limits", "packs", "packsV2", "maxTakenPerPlayer", "refreshInterval", "allowDuplicates"):
        if has_key(block, key):
            add_issue(
                issues,
                "P1",
                block.path,
                first_key_line(block, key),
                f"storeContractsPreset field '{key}' is forbidden; contractOffers uses only maxVisible and groups",
            )

    if not has_key(block, "contractOffers"):
        add_issue(issues, "P1", block.path, block.start_line, "storeContractsPreset must define contractOffers")
        return

    groups = parse_contract_offer_groups(block)
    if not groups:
        add_issue(issues, "P1", block.path, first_key_line(block, "contractOffers"), "contractOffers must define groups")
        return

    seen_pools: set[str] = set()
    for group in groups:
        if not group.pool:
            add_issue(issues, "P1", block.path, group.line, "contractOffers.groups entry must define pool")
            continue

        if group.pool not in offer_pool_ids:
            add_issue(
                issues,
                "P1",
                block.path,
                group.line,
                f"contractOffers group references missing ncContractOfferPool '{group.pool}'",
            )

        if group.pool in seen_pools:
            add_issue(
                issues,
                "P1",
                block.path,
                group.line,
                f"duplicate pool '{group.pool}' inside one contractOffers block is forbidden",
            )
        seen_pools.add(group.pool)

        if group.min_visible < 0:
            add_issue(issues, "P1", block.path, group.line, "contractOffers minVisible must be >= 0")
        if group.max_visible < group.min_visible:
            add_issue(issues, "P1", block.path, group.line, "contractOffers maxVisible must be >= minVisible")
        if group.fill_weight is not None and group.fill_weight <= 0:
            add_issue(issues, "P1", block.path, group.line, "contractOffers fillWeight must be > 0")


def collect_trade_proto_blocks() -> list[ProtoBlock]:
    blocks: list[ProtoBlock] = []
    for path in iter_files(TRADE_YAML_DIRS, {".yml", ".yaml"}):
        blocks.extend(split_proto_blocks(path))
    return blocks


def audit_trade_yaml(issues: list[Issue]) -> None:
    trade_dir = REPO_ROOT / TRADE_RESOURCE_DIR
    if not trade_dir.exists():
        add_issue(issues, "P0", trade_dir, 0, "Trade prototype directory is missing")
        return

    blocks = collect_trade_proto_blocks()
    contract_ids_by_type: dict[str, set[str]] = {
        "ncSupplyContract": set(),
        "ncRetrievalContract": set(),
        "ncHuntContract": set(),
    }
    offer_pool_ids: set[str] = set()

    for block in blocks:
        block_id = proto_id(block)
        if block.type_name in contract_ids_by_type and block_id:
            contract_ids_by_type[block.type_name].add(block_id)
        elif block.type_name == "ncContractOfferPool" and block_id:
            offer_pool_ids.add(block_id)

    for block in blocks:
        if block.type_name == "ncBarterCategory":
            audit_barter_category(block, issues)
        elif block.type_name == "ncSupplyContract":
            audit_supply_contract(block, issues)
        elif block.type_name == "ncRetrievalContract":
            audit_retrieval_contract(block, issues)
        elif block.type_name == "ncRetrievalRoutePreset":
            audit_retrieval_route(block, issues)
        elif block.type_name == "ncHuntContract":
            audit_hunt_contract(block, issues)
        elif block.type_name == "ncContractOfferPool":
            audit_contract_offer_pool(block, issues, contract_ids_by_type)
        elif block.type_name == "storeContractsPreset":
            audit_contract_offers(block, issues, offer_pool_ids)
        elif block.type_name == "ncContractRewardPool":
            add_issue(
                issues,
                "P1",
                block.path,
                block.start_line,
                "legacy ncContractRewardPool is forbidden; use ncSupplyRewardPool",
            )
        elif block.type_name in {"storeContract", "storeContractPack", "ncContractPackV2"}:
            add_issue(
                issues,
                "P1",
                block.path,
                block.start_line,
                f"legacy contract prototype '{block.type_name}' is forbidden; use ncSupplyContract/ncRetrievalContract/ncHuntContract and ncContractOfferPool",
            )


def audit_required_code_shapes(issues: list[Issue]) -> None:
    route_proto = REPO_ROOT / "Content.Shared/_NC/Trade/Domain/Prototypes/NcRetrievalRoutePrototypes.cs"
    if route_proto.exists():
        text = read_text(route_proto)
        if "NcRetrievalClaimMode" not in text or "StoreCargo" not in text or "DestinationProof" not in text:
            add_issue(issues, "P0", route_proto, 1, "NcRetrievalClaimMode must include StoreCargo and DestinationProof")
        if re.search(r"LockDeliveredCargo\s*\{\s*get;\s*set;\s*\}\s*=\s*true\s*;", text):
            add_issue(issues, "P1", route_proto, 1, "LockDeliveredCargo default must remain false until locking is implemented")
        if "Legacy 5.8R field" in text or "route.Proof" in text:
            add_issue(issues, "P1", route_proto, 1, "legacy top-level route proof bridge must not return; use claim.proof")
    else:
        add_issue(issues, "P0", route_proto, 0, "NcRetrievalRoutePrototypes.cs missing")

    retrieval_proto = REPO_ROOT / "Content.Shared/_NC/Trade/Domain/Prototypes/NcRetrievalContractPrototypes.cs"
    if retrieval_proto.exists():
        text = read_text(retrieval_proto)
        for token in (
            "NcRetrievalLegacySpawnTrap",
            "LegacyTargets",
            "LegacyTargetCount",
            "LegacySpawn",
            "DataField(\"targets\")",
            "DataField(\"targetCount\")",
            "DataField(\"spawn\")",
        ):
            if token in text:
                add_issue(issues, "P1", retrieval_proto, 1, f"Retrieval legacy bridge token '{token}' must not return")
    else:
        add_issue(issues, "P0", retrieval_proto, 0, "NcRetrievalContractPrototypes.cs missing")

    hunt_proto = REPO_ROOT / "Content.Shared/_NC/Trade/Domain/Prototypes/NcHuntContractPrototypes.cs"
    if hunt_proto.exists():
        text = read_text(hunt_proto)
        if "NcHuntCompletionMode" not in text or "ConfirmedKill" not in text or "TrophyTurnIn" not in text:
            add_issue(issues, "P0", hunt_proto, 1, "NcHuntCompletionMode must include ConfirmedKill and TrophyTurnIn")
        if "Prototype(\"ncHuntContract\")" not in text:
            add_issue(issues, "P0", hunt_proto, 1, "ncHuntContract prototype definition is missing")
        if "Prototype(\"ncHuntGroup\")" not in text:
            add_issue(issues, "P0", hunt_proto, 1, "ncHuntGroup prototype definition is missing")
    else:
        add_issue(issues, "P0", hunt_proto, 0, "NcHuntContractPrototypes.cs missing")

    retrieval_runtime = REPO_ROOT / "Content.Server/_NC/Trade/Contracts/V2/NcContractSystem.RetrievalV2.cs"
    if retrieval_runtime.exists():
        text = read_text(retrieval_runtime)
        for token in ("ResolveRetrievalProofPresetId", "ResolveRetrievalClaimMode", "route.Proof"):
            if token in text:
                add_issue(issues, "P1", retrieval_runtime, 1, f"Retrieval legacy runtime fallback '{token}' must not return")

    compare = REPO_ROOT / "Content.Server/_NC/Trade/Store/UI/Structured/StoreStructuredSystem.DynamicScratch.Compare.cs"
    if compare.exists():
        text = read_text(compare)
        for token in (
            "SourceHint",
            "DestinationHint",
            "IsRetrievalRoute",
            "RetrievalClaimMode",
            "RetrievalProofIsBearer",
            "OfferPoolId",
            "OfferPoolName",
            "OfferPoolOrder",
            "OfferPoolColor",
        ):
            if token not in text:
                add_issue(issues, "P1", compare, 1, f"ContractEquals should compare route field '{token}'")

    client_data = REPO_ROOT / "Content.Shared/_NC/Trade/Ui/Contracts/ContractClientData.cs"
    if client_data.exists():
        text = read_text(client_data)
        for token in ("OfferPoolId", "OfferPoolName", "OfferPoolOrder", "OfferPoolColor"):
            if token not in text:
                add_issue(issues, "P1", client_data, 1, f"ContractClientData must expose {token} for offer sorting")
        if "Difficulty" in text:
            add_issue(issues, "P1", client_data, 1, "ContractClientData must not expose difficulty; use offer pool metadata")

    server_sort = REPO_ROOT / "Content.Server/_NC/Trade/Store/UI/Structured/StoreStructuredSystem.DynamicState.cs"
    client_sort = REPO_ROOT / "Content.Client/_NC/Trade/NcStoreMenu.Contracts.cs"
    for sort_file in (server_sort, client_sort):
        if not sort_file.exists():
            continue

        text = read_text(sort_file)
        for token in ("DifficultyRank", "GetContractDifficultyOrder", '"легкий"', '"лёгкий"', '"Easy" => 0'):
            if token in text:
                add_issue(issues, "P1", sort_file, 1, "UI contract sorting must not parse hardcoded difficulty strings")
        if "OfferPoolOrder" not in text:
            add_issue(issues, "P1", sort_file, 1, "UI contract sorting must use OfferPoolOrder")

    listing_proto = REPO_ROOT / "Content.Shared/_NC/Trade/Domain/Prototypes/NcStoreListingPrototypes.cs"
    if listing_proto.exists():
        text = read_text(listing_proto)
        for token in (
            "Prototype(\"storeContract\")",
            "StoreContractPrototype",
            "StoreContractPackPrototype",
            "NcContractPackV2Prototype",
            "ContractWeightEntry",
            "PackIncludeEntry",
            "DataField(\"limits\")",
            "DataField(\"packs\")",
            "DataField(\"packsV2\")",
            "DataField(\"maxTakenPerPlayer\")",
            "DataField(\"refreshInterval\")",
            "DataField(\"allowDuplicates\")",
            "DataField(\"difficulty\")",
        ):
            if token in text:
                add_issue(issues, "P1", listing_proto, 1, f"legacy contract offer token '{token}' must not return")

    reward_proto = REPO_ROOT / "Content.Shared/_NC/Trade/Domain/Prototypes/NcRewardPrototypes.cs"
    if reward_proto.exists():
        text = read_text(reward_proto)
        for token in ("Prototype(\"ncContractRewardPool\")", "NcContractRewardPoolPrototype"):
            if token in text:
                add_issue(issues, "P1", reward_proto, 1, f"legacy reward pool token '{token}' must not return")

    refresh = REPO_ROOT / "Content.Server/_NC/Trade/Contracts/Refresh/NcContractSystem.Refresh.cs"
    if refresh.exists():
        text = read_text(refresh)
        for token in (
            "MergeDifficultyLimits",
            "ProcessDifficulty",
            "BuildCandidatePool",
            "CollectFromPackRecursive",
            "CollectFromV2PackRecursive",
            "TryIssueDifficultyContract",
            "GetCooldownState",
        ):
            if token in text:
                add_issue(issues, "P1", refresh, 1, f"legacy difficulty/pack generation token '{token}' must not return")

    generation = REPO_ROOT / "Content.Server/_NC/Trade/Contracts/Generation/NcContractSystem.Generate.cs"
    if generation.exists():
        text = read_text(generation)
        for token in ("ContractPoolCandidateKind.Legacy", "StoreContractPrototype", "BuildWeightedContractTargets", "candidate.Difficulty"):
            if token in text:
                add_issue(issues, "P1", generation, 1, f"legacy storeContract generation token '{token}' must not return")

    server_contract = REPO_ROOT / "Content.Shared/_NC/Trade/Domain/Contracts/ContractServerData.cs"
    if server_contract.exists() and "Difficulty" in read_text(server_contract):
        add_issue(issues, "P1", server_contract, 1, "ContractServerData must not carry difficulty; use offer pool metadata")


def main() -> int:
    issues: list[Issue] = []

    audit_no_live_exchange(issues)
    audit_trade_yaml(issues)
    audit_required_code_shapes(issues)

    if issues:
        print("NC Trade Core audit: FAIL")
        print()
        for issue in issues:
            line = f":{issue.line}" if issue.line else ""
            print(f"{issue.severity}: {issue.path}{line}: {issue.message}")
        return 1

    print("NC Trade Core audit: OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
