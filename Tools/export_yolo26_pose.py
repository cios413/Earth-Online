"""Export the official YOLO26n-Pose inference head for Unity Sentis.

Checkpoint: https://github.com/ultralytics/assets/releases/download/v8.4.0/yolo26n-pose.pt
Ultralytics distributes the checkpoint under AGPL-3.0.
"""

import argparse
from pathlib import Path

import torch
from ultralytics import YOLO
from ultralytics.nn.modules import C2f


class RawPose(torch.nn.Module):
    def __init__(self, model):
        super().__init__()
        self.layers = model.model
        self.saved_layers = set(model.save)

    def forward(self, image):
        saved = []
        value = image
        for layer in self.layers[:-1]:
            if layer.f != -1:
                value = (
                    saved[layer.f] if isinstance(layer.f, int)
                    else [value if index == -1 else saved[index] for index in layer.f]
                )
            value = layer.forward_split(value) if isinstance(layer, C2f) else layer(value)
            saved.append(value if layer.i in self.saved_layers else None)
        head = self.layers[-1]
        levels = [saved[index] for index in head.f]
        # The checkpoint sets end2end=False, so its inference path uses one2many.
        # kpts_sigma is a training-only uncertainty output and is not used by Pose26 inference.
        raw = head.forward_head(levels, **head.one2many)
        return raw["boxes"], raw["scores"], raw["kpts"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("weights", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    torch.set_num_threads(2)
    model = YOLO(args.weights).model.float().eval()
    head = model.model[-1]
    if head.reg_max != 1 or tuple(head.kpt_shape) != (17, 3) or model.names != {0: "person"}:
        raise ValueError("Expected the official one-class YOLO26n-Pose checkpoint with 17x3 keypoints")
    if head.end2end:
        raise ValueError("This decoder expects the checkpoint's one-to-many inference branch")
    with torch.inference_mode():
        torch.onnx.export(
            RawPose(model).eval(), torch.zeros(1, 3, 416, 416), args.output,
            input_names=["image"], output_names=["boxes_raw", "scores_raw", "pose_raw"],
            opset_version=12,
        )


if __name__ == "__main__":
    main()
