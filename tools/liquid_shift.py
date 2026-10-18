"""Measure how far each liquid render moved between its two probe captures (camera moved +2 units).

Expected for world-anchored content: 2 units * (512 px / 24 units) = 42.7 px. A screen-anchored
pattern shows ~0 px. Usage: liquid_shift.py <probe run dir>
"""
import glob, os, sys
import numpy as np
from PIL import Image

EXPECTED = 2 * 512 / 24.0
run = sys.argv[1]
for a in sorted(glob.glob(os.path.join(run, "liquid-*-0.png"))):
    b = a[:-6] + "-1.png"
    A = np.asarray(Image.open(a).convert("L"), dtype=float)
    B = np.asarray(Image.open(b).convert("L"), dtype=float)
    if A.std() < 2:
        print(f"{os.path.basename(a)[7:-6]:45s} blank (nothing rendered in frame)")
        continue
    # Camera moved +x, so world content moves -x in image B: B[x] ~ A[x + d]
    errs = {d: np.abs(A[:, 60 + d:452 + d] - B[:, 60:452]).mean() for d in range(-60, 61)}
    best = min(errs, key=errs.get)
    print(f"{os.path.basename(a)[7:-6]:45s} best shift {best:+4d} px (world-anchored = {EXPECTED:.1f}); err at 0: {errs[0]:.2f}, at best: {errs[best]:.2f}")
