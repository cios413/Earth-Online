"""Export Ultralytics YOLOv8n-seg weights for Unity Barracuda (opset 12).

Tested with Ultralytics 8.3.0 and PyTorch 2.5.1. The source checkpoint is
https://github.com/ultralytics/assets/releases/download/v8.3.0/yolov8n-seg.pt
and is distributed by Ultralytics under AGPL-3.0.
"""

import argparse
from pathlib import Path

import torch
from ultralytics import YOLO
from ultralytics.nn.modules import C2f


class PersonSegmentation(torch.nn.Module):
    def __init__(self, model):
        super().__init__()
        self.layers = model.model
        self.saved_layers = set(model.save)
        for classifier in self.layers[-1].cv3:
            logits = classifier[-1]
            person = torch.nn.Conv2d(logits.in_channels, 1, kernel_size=1)
            with torch.no_grad():
                person.weight.copy_(logits.weight[:1])
                person.bias.copy_(logits.bias[:1])
            classifier[-1] = person

    def forward(self, image):
        features = []
        value = image
        for layer in self.layers[:-1]:
            if layer.f != -1:
                value = (
                    features[layer.f]
                    if isinstance(layer.f, int)
                    else [value if index == -1 else features[index] for index in layer.f]
                )
            value = layer.forward_split(value) if isinstance(layer, C2f) else layer(value)
            features.append(value if layer.i in self.saved_layers else None)

        head = self.layers[-1]
        levels = [features[index] for index in head.f]
        outputs = [
            torch.cat(
                (head.cv2[index](level), head.cv3[index](level), head.cv4[index](level)),
                dim=1,
            )
            for index, level in enumerate(levels)
        ]
        return (*outputs, head.proto(levels[0]))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("weights", type=Path, help="Official COCO yolov8n-seg.pt")
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "Assets/Resources/Models/yolov8n-person-seg.onnx",
    )
    args = parser.parse_args()
    torch.set_num_threads(2)
    model = YOLO(args.weights).model.float().eval().fuse()
    if model.names[0] != "person":
        raise ValueError("Expected a COCO model with person as class 0")
    model = PersonSegmentation(model).eval()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with torch.inference_mode():
        torch.onnx.export(
            model,
            torch.zeros(1, 3, 416, 416),
            args.output,
            input_names=["image"],
            output_names=["stride8", "stride16", "stride32", "prototypes"],
            opset_version=12,
        )


if __name__ == "__main__":
    main()
