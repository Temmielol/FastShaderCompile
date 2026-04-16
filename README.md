# FastShaderCompile

A Unity editor tool that makes your shaders compile faster. Drop a shader into the window, pick what you want it to do, and from then on Unity compiles a faster version of your shader instead of the original. Your actual shader file on disk never changes.

## Why this exists

Big shaders take a while to compile. Every time you save, Unity has to chew through the whole thing, and on shaders with lots of `if (_Property)` branches and deep include chains this can turn into multiple minutes per edit. That adds up fast when you are iterating.

FastShaderCompile cuts down that editor compile time by rewriting the shader right before Unity compiles it. It pulls all your custom `.cginc` includes into one file and converts property toggles into `shader_feature` variants, both of which the compiler can chew through faster. Your shader file on disk stays exactly how you wrote it.

This is purely a development-time thing. And should not replace a custom optimizer as you cannot animate shader_features at runtime. We get away with using `shader_feature` here because you are sitting in the editor flipping toggles and saving, not animating them, so the tradeoff does not matter while iterating.

TLDR if you want to animate toggles ship your branched version with an optimizer.

## What it actually does

You can turn these on per shader:

**Inline includes**

If your shader splits its code across a bunch of `.cginc` files, this pulls them all into one big shader before compiling. Unity built-in includes like `UnityCG.cginc` are left alone. This reduces how many files the compiler has to open and parse, which speeds up compiles on large shaders with lots of includes.

If something breaks, you still get proper error messages pointing at the right file and line number in your original source, because the tool adds `#line` markers for you.

**if to shader_feature**

Takes property toggle branches like:

```hlsl
if (_OldGlow)
{
    col.rgb += glow;
}
```

And rewrites them as:

```hlsl
#if _OLDGLOW_ON
    col.rgb += glow;
#endif
```

Then adds the right `#pragma shader_feature_local` line so Unity knows about the keyword. It also fixes up your `[Toggle]`, `[ToggleOff]`, and `[SubToggle]` attributes so the checkboxes in the material inspector turn the keyword on and off.

The tool only does this for properties that have one of those Toggle attributes. Sliders and plain floats are left alone, so it will not silently break any of your other `if` statements.

## Setup

1. Download `FastShaderCompile.cs` and drop it in your project under any `Editor` folder, for example `Assets/Editor/FastShaderCompile.cs`. It has to be under an `Editor` folder, otherwise Unity will try to ship it.
2. Open the window from the Unity menu bar: `Temmie > Fast Shader Compile`.
3. Drag any shader from your Project window into the drop zone.
4. Tick the boxes for what you want it to do.

That is it. From now on, every time that shader gets reimported (which happens whenever you save it), the transforms run automatically.

## Using the window

- **Drop zone at the top:** drag shaders here to track them.
- **Search bar:** filters the list by name.
- **Checkbox on the left of each row:** turns the tool on or off for that shader without removing it.
- **Inline includes / if to shader_feature:** per-shader toggles for each transform.
- **Export:** saves a copy of the transformed shader next to the original, named `YourShader_compiled.shader`. Useful for seeing exactly what the transforms produced.
- **Reimport:** forces Unity to reimport the shader right now instead of waiting for the next save.
- **Ping:** highlights the shader in the Project window.
- **X:** removes the shader from the list. Does not delete any files.

The list of tracked shaders is stored per machine using Unity's EditorPrefs, keyed by the shader's GUID so renaming or moving a shader will not lose your settings.

## When to use it

**You have a big shader with a lot of toggle properties.** This is the main use case. Toon shaders, eye shaders, anything with a "features" section in the inspector full of checkboxes. Turn on "if to shader_feature" and compile times drop, often by a lot.

**Your shader is split across a pile of `.cginc` files.** Turn on "Inline includes." Less impactful than the feature conversion but helps on shaders with really deep include trees.

**You want to see what the tool actually produces.** Hit Export. You get a `.shader` file next to the original with all the transforms applied, so you can read through it or use it for debugging.

**You are actively working on a shader and want fast iteration.** Leave both transforms enabled. Every time you save, the shader gets transformed and recompiled. If there is a syntax error, Unity points you at the right line in your original file thanks to the `#line` markers.

## Notes

- Works on shaders written in regular ShaderLab with CGPROGRAM or HLSLPROGRAM blocks. Does not work on Shader Graph.
- Tested on Unity 2022.3. Should work on most nearby versions. If you hit issues on older or newer Unity, the reflection call on `ShaderUtil.UpdateShaderAsset` is the part most likely to need tweaking.
- Only top-level `if (_Prop)` blocks are converted. If you have nested toggle ifs, only the outer one gets rewritten. Usually fine but worth knowing.
- If you already wrote `[Toggle(_MY_KEYWORD)]` with your own keyword name, the tool leaves it alone. Let the tool name the keyword for you, or do that property manually.

## License

MIT.
