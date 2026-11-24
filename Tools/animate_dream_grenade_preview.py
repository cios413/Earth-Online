"""Assemble the actual Unity-rendered splash frames into a looping preview."""
from pathlib import Path
import tempfile
import json
from PIL import Image

folder = Path(tempfile.gettempdir()) / 'opencode/dream-liquid-frames'
output = Path(__file__).resolve().parents[1] / 'Assets/BodyThrow/ArtPreviews/DreamGrenadeSplash.gif'
frames = [Image.open(path).convert('RGB') for path in sorted(folder.glob('frame-*.png'))]
if len(frames) != 66:
    raise RuntimeError('Run DreamGrenadeValidation.Validate in Unity to render all 66 frames first.')
timing_path = folder / 'timing.json'
timing = json.loads(timing_path.read_text()) if timing_path.exists() else {'frameMilliseconds': 56, 'heroFrame': 25}
frames[timing['heroFrame']].save(output.with_suffix('.png'))
# One palette avoids color pumping between neighboring animation frames.
atlas = Image.new('RGB', (512*6,512*11))
for i, frame in enumerate(frames):
    atlas.paste(frame, ((i%6)*512,(i//6)*512))
palette = atlas.resize((768,1408)).quantize(colors=256)
indexed = [frame.quantize(palette=palette,dither=Image.Dither.FLOYDSTEINBERG) for frame in frames]
indexed[0].save(output,save_all=True,append_images=indexed[1:],duration=timing['frameMilliseconds'],loop=0,disposal=2)
print(output)
