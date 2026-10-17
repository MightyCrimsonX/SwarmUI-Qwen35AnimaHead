# SwarmUI-Qwen35AnimaHead

A lightweight SwarmUI extension and wrapper bridge that provides seamless integration for the ComfyUI custom node repository [comfyui-qwen35-anima-head](https://github.com/nawka12/comfyui-qwen35-anima-head) developed by [nawka12](https://github.com/nawka12).

---

## Overview

When generating with **Anima** models, users can choose to run the high-performance **Qwen3.5-0.8B + head** text encoder (`qwen_3_5_08b_anima_head_L20.safetensors` or `qwen_3_5_08b_imgloss_*.safetensors`) in place of standard Qwen3-0.6B.

Standard ComfyUI/SwarmUI `CLIPLoader` nodes cannot load these weights directly due to architectural differences (bare state dict keys, 20 transformer layers instead of 24, and a specialized projection head replacing the final norm layer).

This extension acts as a native SwarmUI port/adapter:
- Automatically detects when a Qwen3.5 Anima Head text encoder is selected.
- Routes the workflow to `Qwen35AnimaCLIPLoader` instead of the default CLIP loader.
- Outputs a standard ComfyUI `CLIP` reference compatible with stock `CLIPTextEncode` and LoRA loaders.
- **Zero-touch auto-setup**: Automatically clones the underlying ComfyUI node pack into your ComfyUI backend's `custom_nodes` directory if not already installed.

---

## Features

- **Automated Custom Node Installation**: Automatically checks if `comfyui-qwen35-anima-head` is installed in the active ComfyUI backend's `custom_nodes` folder. If missing, it automatically performs a `git clone` and reloads the backend.
- **Smart Model Detection**: Automatically identifies Qwen3.5 Anima text encoders by filename (`anima_head`, `imgloss`, `qwen_3_5...anima`, etc.) selected via the standard **Qwen Model** parameter.
- **Dedicated Controls**: Exposes optional explicit settings under the `Anima Qwen 3.5` parameter group:
  - `[Anima] Qwen 3.5 Text Encoder`: Explicitly pick your Qwen 3.5 text encoder file from your Clip models folder.
  - `[Anima] Force Qwen 3.5 Anima Loader`: Force the use of `Qwen35AnimaCLIPLoader` regardless of filename.
  - `[Anima] Qwen 3.5 Loader Device`: Choose between `default` (GPU/accelerator) and `cpu`.

---

## Requirements & Installation

1. Place this extension directory into SwarmUI's extensions folder:
   ```
   SwarmUI/src/Extensions/SwarmUI-Qwen35AnimaHead/
   ```
2. Download the text encoder model (such as `qwen_3_5_08b_anima_head_L20.safetensors` from [Civitai](https://civitai.com/models/3003960)) and place it in your `Models/clip/` or `ComfyUI/models/text_encoders/` folder.
3. Start or restart SwarmUI.

---

## Usage

1. Load your **Anima** diffusion model.
2. Under **Advanced Model Addons** -> **Qwen Model** (or under **Anima Qwen 3.5** -> **[Anima] Qwen 3.5 Text Encoder**), select `qwen_3_5_08b_anima_head_L20.safetensors`.
3. Generate! SwarmUI will seamlessly use the `Qwen35AnimaCLIPLoader` custom node in your ComfyUI workflow.

---

## Credits

- Original ComfyUI custom node implementation: [comfyui-qwen35-anima-head](https://github.com/nawka12/comfyui-qwen35-anima-head) by [nawka12](https://github.com/nawka12).
