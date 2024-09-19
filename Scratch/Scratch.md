# Clipmap

Toroidal Update

offset (in clip level space)

newOffset (in clip level space)

clipcenter (in texture space)

newClipCenter (in texture space)

clipSize (by texels, in both textures space and clip level space)

halfSize (by texels, in both textures space and clip level space)

## updated zones

<img src="ToroidalUpdate.png" alt="drawing" width="512"/>

newClipCenter may be outside 

unity ok with copying 0 area
### Zone 1
        Src (in texture space)
            +X+Y min: clipCenter.xy + halfsize.xy + offset.y
            +X+Y max: clipCenter.xy + halfsize.xy + offset.y + newoffset.xy

            +X-Y min: clipCenter.xy + halfsize.xy + offset.y
            +X-Y max: clipCenter.xy + halfsize.xy + offset.y + newoffset.xy
        Dst (in clipLevel space)
            min: offset.y
            max: offset.xy + newoffset.y

### Zone 2
    
    Src (in texture space)
        min: clipCenter.xy + halfsize.xy + offset.y
        max: clipCenter.xy + halfsize.xy + offset.y + newoffset.xy
    Dst (in clipLevel space)
        min: offset.y
        max: offset.xy + newoffset.y
2. 
3. 
4. 
5. 