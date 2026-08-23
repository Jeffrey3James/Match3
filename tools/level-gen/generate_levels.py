#!/usr/bin/env python3
"""Deterministically generate Xandria Gem Jam's authored level catalog.

The script deliberately reads and retains the first five levels in levels.json before
replacing the catalog.  Those tutorial records are authored seed data and must stay
unchanged.  Run this file from any working directory; its paths are repository-relative.
"""
from __future__ import annotations

import json
import math
import random
from pathlib import Path
from typing import Iterable

REPO = Path(__file__).resolve().parents[2]
LEVELS_PATH = REPO / "Assets" / "Resources" / "Levels" / "levels.json"
SEED = 20260822
WIDTH, HEIGHT = 9, 13
GEMS = ["SquareGem", "ButterflyGem", "HeartGem", "TriangleGem", "GreenTriangleGem", "CircleGem"]
OBSTACLES = ["Ice", "WitheredVine", "Bubble", "Mushroom", "Grass"]
TIER_BUFFER = {"tutorial": 15, "easy": 12, "medium": 8, "hard": 5, "hardest": 3}


def cell(x: int, y: int) -> dict:
    return {"x": x, "y": y}


def level_tier(level_id: int) -> str:
    """Tier ramp: one short onboarding extension, then steadily tighter windows."""
    if level_id <= 10:
        return "tutorial"
    if level_id <= 55:
        return "easy"
    if level_id <= 110:
        return "medium"
    if level_id <= 180:
        return "hard"
    return "hardest"


def amount_range(tier: str) -> tuple[int, int]:
    return {
        "tutorial": (3, 8),
        "easy": (8, 15),
        "medium": (15, 25),
        "hard": (25, 40),
        "hardest": (40, 60),
    }[tier]


def min_moves(level: dict) -> int:
    """Estimated perfect-play lower bound requested in the level-design brief."""
    objective_work = math.ceil(sum(item["amount"] for item in level["objectives"]) / 3)
    obstacle_work = math.ceil(
        sum(obstacle["health"] * len(obstacle["cells"]) for obstacle in level["obstacles"]) / 2
    )
    excluded_count = len(level["excludedCells"])
    shape_penalty = 2 if excluded_count > 35 else 1 if excluded_count > 20 else 0
    return objective_work + obstacle_work + shape_penalty


def make_shape(shape_name: str) -> list[dict]:
    """Return a playable-board-safe 9 x 13 cutout; every shape leaves >= 60% playable."""
    excluded: set[tuple[int, int]] = set()
    if shape_name == "diamond":
        # Trim three cells from each corner: 24 removed / 93 playable.
        for x, y in [(x, y) for x in range(WIDTH) for y in range(HEIGHT)]:
            if (x < 3 and y < 3 and x + y < 3) or (x > 5 and y < 3 and (8 - x) + y < 3):
                excluded.add((x, y))
            if (x < 3 and y > 9 and x + (12 - y) < 3) or (x > 5 and y > 9 and (8 - x) + (12 - y) < 3):
                excluded.add((x, y))
    elif shape_name == "hourglass":
        # Narrow the waist but preserve broad top and bottom rooms.
        for y in range(4, 9):
            for x in (0, 1, 7, 8):
                excluded.add((x, y))
        for y in (5, 6, 7):
            excluded.add((2, y))
            excluded.add((6, y))
    elif shape_name == "cross":
        # Four corner blocks imply a plus-shaped central play space (32 removed / 85 playable).
        for x in range(WIDTH):
            for y in range(HEIGHT):
                if (x in (0, 1) and y < 4) or (x in (7, 8) and y < 4) or (x in (0, 1) and y > 8) or (x in (7, 8) and y > 8):
                    excluded.add((x, y))
    elif shape_name == "lshape":
        # Remove one seven-row quadrant: 21 removed / 96 playable.
        for x in range(3):
            for y in range(7):
                excluded.add((x, y))
    elif shape_name == "staircase":
        # Progressive left-side steps, totaling 24 removed cells.
        widths = [4, 4, 3, 3, 2, 2, 1, 1, 1, 1, 1, 1, 0]
        for y, row_width in enumerate(widths):
            for x in range(row_width):
                excluded.add((x, y))
    elif shape_name == "split":
        # A thin central divider creates two tactical chambers (13 removed / 104 playable).
        for y in range(HEIGHT):
            excluded.add((4, y))
    else:
        raise ValueError(f"Unknown board shape: {shape_name}")
    return [cell(x, y) for x, y in sorted(excluded, key=lambda point: (point[1], point[0]))]


def choose_shape(level_id: int) -> list[dict]:
    """Interleave shaped boards after the vine reveal; one rectangular breather in three."""
    if level_id < 76 or level_id % 3 == 0:
        return []
    shapes = ["diamond", "hourglass", "cross", "lshape", "staircase", "split"]
    return make_shape(shapes[(level_id - 76) % len(shapes)])


def neighbors(point: tuple[int, int]) -> Iterable[tuple[int, int]]:
    x, y = point
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nx, ny = x + dx, y + dy
        if 0 <= nx < WIDTH and 0 <= ny < HEIGHT:
            yield nx, ny


def line_cells(count: int, available: set[tuple[int, int]], rng: random.Random) -> list[tuple[int, int]] | None:
    candidates = list(available)
    rng.shuffle(candidates)
    directions = [(1, 0), (-1, 0), (0, 1), (0, -1)]
    rng.shuffle(directions)
    for x, y in candidates:
        for dx, dy in directions:
            run = [(x + dx * step, y + dy * step) for step in range(count)]
            if all(point in available for point in run):
                return run
    return None


def cluster_cells(count: int, available: set[tuple[int, int]], rng: random.Random) -> list[tuple[int, int]]:
    """Grow a contiguous patch. All catalog shapes have ample room for requested sizes."""
    candidates = list(available)
    rng.shuffle(candidates)
    for start in candidates:
        result = [start]
        used = {start}
        while len(result) < count:
            frontier = [n for point in result for n in neighbors(point) if n in available and n not in used]
            if not frontier:
                break
            nxt = rng.choice(frontier)
            result.append(nxt)
            used.add(nxt)
        if len(result) == count:
            return result
    # This is a defensive fallback, not expected with the catalog's conservative cuts.
    return rng.sample(sorted(available), count)


def place_cells(count: int, available: set[tuple[int, int]], rng: random.Random, pattern: str) -> list[dict]:
    if count > len(available):
        raise ValueError("Requested more obstacle cells than the board has available")
    points = line_cells(count, available, rng) if pattern == "line" else None
    if points is None:
        points = cluster_cells(count, available, rng)
    for point in points:
        available.remove(point)
    return [cell(x, y) for x, y in points]


def add_obstacle(obstacles: list[dict], available: set[tuple[int, int]], obstacle_type: str, health: int, count: int, rng: random.Random, pattern: str) -> None:
    obstacles.append({
        "type": obstacle_type,
        "health": health,
        "cells": place_cells(count, available, rng, pattern),
    })


def objective_count(level_id: int) -> int:
    if level_id <= 50:
        return 1
    if level_id <= 75:
        return 2 if level_id >= 55 else 1
    if level_id <= 125:
        return 2
    if level_id <= 150:
        return 3 if level_id % 2 == 0 else 2
    return 3


def build_objectives(level_id: int, rng: random.Random) -> list[dict]:
    tier = level_tier(level_id)
    low, high = amount_range(tier)
    count = objective_count(level_id)
    gems = list(GEMS)
    rng.shuffle(gems)
    # The occasional boss has the upper end of its tier, while all values remain in-tier.
    boss = level_id % 12 == 0 or level_id % 25 == 0
    return [
        {"gemType": gems[index], "amount": high - (index % 2) if boss else rng.randint(low, high)}
        for index in range(count)
    ]


def build_obstacles(level_id: int, available: set[tuple[int, int]], rng: random.Random) -> list[dict]:
    """Mechanics arrive in the requested order and become denser only after practice."""
    obstacles: list[dict] = []
    boss = level_id % 12 == 0 or level_id % 25 == 0
    extra = 1 if boss else 0

    if level_id <= 15:
        return obstacles
    if level_id <= 30:
        add_obstacle(obstacles, available, "Bubble", 1 + (level_id % 2), 2 + (level_id % 3), rng, "cluster")
    elif level_id <= 50:
        # The first half is Ice-only; Bubble returns afterward as familiar support pressure.
        add_obstacle(obstacles, available, "Ice", 2, 3 + (level_id % 4) + extra, rng, "line")
        if level_id >= 38:
            add_obstacle(obstacles, available, "Bubble", 1 + (level_id % 2), 2 + (level_id % 3), rng, "cluster")
    elif level_id <= 75:
        # Grass always appears as a cluster rather than isolated single cells.
        add_obstacle(obstacles, available, "Grass", 1, 4 + (level_id % 5) + extra, rng, "cluster")
        if level_id % 2 == 0:
            add_obstacle(obstacles, available, "Ice", 2, 3 + (level_id % 4), rng, "line")
        else:
            add_obstacle(obstacles, available, "Bubble", 1 + (level_id % 2), 3 + (level_id % 3), rng, "cluster")
    elif level_id <= 110:
        # Vines are visibly linear; the supporting obstacle rotates each encounter.
        add_obstacle(obstacles, available, "WitheredVine", 3, 5 + (level_id % 5) + extra, rng, "line")
        support = ["Grass", "Ice", "Bubble"][(level_id - 76) % 3]
        health = {"Grass": 1, "Ice": 2, "Bubble": 1 + (level_id % 2)}[support]
        add_obstacle(obstacles, available, support, health, 4 + (level_id % 4), rng, "cluster" if support != "Ice" else "line")
        if boss:
            add_obstacle(obstacles, available, "Mushroom", 3, 4 + (level_id % 3), rng, "cluster")
    elif level_id <= 150:
        # Exactly two obstacle types per level: a focused combination-practice band.
        pairs = [("WitheredVine", "Ice"), ("Mushroom", "Grass"), ("WitheredVine", "Bubble"), ("Ice", "Mushroom")]
        first, second = pairs[(level_id - 111) % len(pairs)]
        for obstacle_type, count in ((first, 7 + (level_id % 4) + extra), (second, 6 + ((level_id + 1) % 4))):
            health = {"WitheredVine": 3, "Ice": 2, "Bubble": 2, "Mushroom": 3, "Grass": 1}[obstacle_type]
            pattern = "line" if obstacle_type in {"WitheredVine", "Ice"} else "cluster"
            add_obstacle(obstacles, available, obstacle_type, health, count, rng, pattern)
    elif level_id <= 200:
        # Three types in every late-game encounter, with a fourth at 12-level bosses.
        rotations = [
            ("WitheredVine", "Ice", "Grass"),
            ("Mushroom", "Bubble", "WitheredVine"),
            ("Ice", "Grass", "Mushroom"),
        ]
        types = list(rotations[(level_id - 151) % len(rotations)])
        if boss:
            types.append("Bubble" if "Bubble" not in types else "Ice")
        for index, obstacle_type in enumerate(types):
            health = {"WitheredVine": 3, "Ice": 2, "Bubble": 2, "Mushroom": 3, "Grass": 1}[obstacle_type]
            count = 7 + ((level_id + index * 2) % 4) + (1 if boss and index == 0 else 0)
            pattern = "line" if obstacle_type in {"WitheredVine", "Ice"} else "cluster"
            add_obstacle(obstacles, available, obstacle_type, health, count, rng, pattern)
    else:
        # Boss band: four types every level, all five on recurring milestone encounters.
        rotations = [
            ("WitheredVine", "Ice", "Mushroom", "Bubble"),
            ("WitheredVine", "Grass", "Ice", "Mushroom"),
            ("Mushroom", "Bubble", "WitheredVine", "Grass"),
        ]
        types = list(rotations[(level_id - 201) % len(rotations)])
        if boss:
            missing = next(item for item in OBSTACLES if item not in types)
            types.append(missing)
        for index, obstacle_type in enumerate(types):
            health = {"WitheredVine": 3, "Ice": 2, "Bubble": 2, "Mushroom": 3, "Grass": 1}[obstacle_type]
            count = 8 + ((level_id + index) % 4) + (1 if boss and index < 2 else 0)
            pattern = "line" if obstacle_type in {"WitheredVine", "Ice"} else "cluster"
            add_obstacle(obstacles, available, obstacle_type, health, count, rng, pattern)
    return obstacles


def build_level(level_id: int) -> dict:
    rng = random.Random(SEED + level_id * 1009)
    excluded_cells = choose_shape(level_id)
    excluded = {(entry["x"], entry["y"]) for entry in excluded_cells}
    available = {(x, y) for x in range(WIDTH) for y in range(HEIGHT)} - excluded
    level = {
        "id": level_id,
        "name": f"Level {level_id}",
        "maxMoves": 0,
        "width": WIDTH,
        "height": HEIGHT,
        "excludedCells": excluded_cells,
        "objectives": build_objectives(level_id, rng),
        "obstacles": build_obstacles(level_id, available, rng),
    }
    level["maxMoves"] = min_moves(level) + TIER_BUFFER[level_tier(level_id)]
    return level


def validate(data: dict, seeds: list[dict]) -> None:
    assert data["version"] == 1
    levels = data["levels"]
    assert len(levels) == 250, f"Expected 250 levels, got {len(levels)}"
    assert [level["id"] for level in levels] == list(range(250)), "IDs are not sequential 0..249"
    assert levels[:5] == seeds, "Tutorial levels 0–4 changed"

    for level in levels:
        level_id = level["id"]
        assert level["name"] == f"Level {level_id}"
        assert level["width"] == WIDTH and level["height"] == HEIGHT
        assert level["objectives"] or level["obstacles"], f"Level {level_id} is empty"
        assert level["maxMoves"] >= min_moves(level), f"Level {level_id} maxMoves is below its computed minimum"
        excluded = {(entry["x"], entry["y"]) for entry in level["excludedCells"]}
        assert len(excluded) == len(level["excludedCells"])
        assert all(0 <= x < WIDTH and 0 <= y < HEIGHT for x, y in excluded)
        seen_cells: set[tuple[int, int]] = set()
        total_obstacle_cells = 0
        for objective in level["objectives"]:
            assert objective["gemType"] in GEMS
            assert objective["amount"] > 0
        for obstacle in level["obstacles"]:
            assert obstacle["type"] in OBSTACLES
            assert 1 <= obstacle["health"] <= 5
            assert 1 <= len(obstacle["cells"]) <= 15
            for entry in obstacle["cells"]:
                point = (entry["x"], entry["y"])
                assert 0 <= point[0] < WIDTH and 0 <= point[1] < HEIGHT
                assert point not in excluded, f"Level {level_id} obstacle placed in excluded cell"
                assert point not in seen_cells, f"Level {level_id} overlaps obstacle cells"
                seen_cells.add(point)
                total_obstacle_cells += 1
        assert total_obstacle_cells <= 60

        if level_id >= 5:
            tier = level_tier(level_id)
            low, high = amount_range(tier)
            assert all(low <= objective["amount"] <= high for objective in level["objectives"])
            assert level["maxMoves"] == min_moves(level) + TIER_BUFFER[tier]
        if 5 <= level_id <= 15:
            assert not level["obstacles"]
        elif 16 <= level_id <= 30:
            assert {item["type"] for item in level["obstacles"]} == {"Bubble"}
        elif 31 <= level_id <= 50:
            assert "Ice" in {item["type"] for item in level["obstacles"]}
            assert {item["type"] for item in level["obstacles"]} <= {"Ice", "Bubble"}
        elif 51 <= level_id <= 75:
            assert "Grass" in {item["type"] for item in level["obstacles"]}
        elif 76 <= level_id <= 110:
            assert "WitheredVine" in {item["type"] for item in level["obstacles"]}
        elif 111 <= level_id <= 150:
            assert len({item["type"] for item in level["obstacles"]}) == 2
        elif 151 <= level_id <= 200:
            assert len({item["type"] for item in level["obstacles"]}) >= 3
        elif level_id >= 201:
            assert len({item["type"] for item in level["obstacles"]}) >= 4


def main() -> None:
    source = json.loads(LEVELS_PATH.read_text(encoding="utf-8"))
    seeds = source["levels"][:5]
    assert len(seeds) == 5 and [item["id"] for item in seeds] == [0, 1, 2, 3, 4]
    collection = {"version": 1, "levels": seeds + [build_level(level_id) for level_id in range(5, 250)]}
    validate(collection, seeds)
    LEVELS_PATH.write_text(json.dumps(collection, indent=2) + "\n", encoding="utf-8")
    # Required round-trip validation: read the authored JSON back through Python's parser.
    round_tripped = json.loads(LEVELS_PATH.read_text(encoding="utf-8"))
    validate(round_tripped, seeds)
    print("250 levels, all ids sequential, all maxMoves >= computed min")


if __name__ == "__main__":
    main()
