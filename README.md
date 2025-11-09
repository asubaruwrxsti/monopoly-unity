# monopoly-unity

# make the textures readable
```bash
find "Assets/Resources" -type f -name "*.png.meta" -exec sh -c '
  sed -i "" "s/isReadable: 0/isReadable: 1/g" "$1"
  sed -i "" "s/textureFormat: 1/textureFormat: 4/g" "$1"
  sed -i "" "s/textureCompression: 1/textureCompression: 0/g" "$1"
  sed -i "" "s/textureFormat: -1/textureFormat: 4/g" "$1"
  echo "Updated: $(basename "$1")"
' sh {} \;
```