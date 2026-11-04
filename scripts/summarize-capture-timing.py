"""Summarize metadata-only capture benchmark JSONL (never consumes images or configuration)."""
import argparse
import json
import math
import statistics
from collections import defaultdict


def percentiles(values):
    ordered = sorted(values)
    return {"n": len(ordered), "p50_ms": round(statistics.median(ordered), 2),
            "p95_ms": round(ordered[math.ceil(len(ordered) * 0.95) - 1], 2)}


def summarize(lines):
    rows = [json.loads(line) for line in lines if line.strip()]
    runs = [row for row in rows if "sample" in row]
    durations = defaultdict(list)
    rendered, ready, png_sizes = [], [], []
    groups = defaultdict(list)
    for run in runs:
        stages = run["stages"]
        png_sizes.append(max(s["PngBytes"] for s in stages))
        sums = defaultdict(float)
        for stage in stages:
            sums[stage["Stage"]] += stage["DurationMs"]
        for stage, duration in sums.items():
            durations[stage].append(duration)
        frame = max(s["TotalMs"] for s in stages if s["Stage"] == "overlay_first_frame_rendered")
        rendered.append(frame)
        ready.append(next(s["TotalMs"] for s in stages if s["Stage"] == "all_overlays_input_ready"))
        groups[stages[0]["Stage"]].append(frame)
    return {"samples": len(runs), "environment": next(row["environment"] for row in rows if "environment" in row),
            "desktop_png_bytes": {"minimum": min(png_sizes), "maximum": max(png_sizes), "median": statistics.median(png_sizes)},
            "shell_restored_every_sample": all(run["shellRestored"] for run in runs),
            "first_sample_rendered_ms": round(rendered[0], 2),
            "all_monitors_rendered": percentiles(rendered), "input_ready": percentiles(ready),
            "repeated_rendered": percentiles(rendered[1:]) if len(rendered) > 1 else None,
            "trigger_groups": {name: percentiles(values) for name, values in groups.items()},
            "stage_duration_sum_per_capture": {name: percentiles(values) for name, values in durations.items()}}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=argparse.FileType("r", encoding="utf-8-sig"))
    args = parser.parse_args()
    print(json.dumps(summarize(args.input), indent=2))
