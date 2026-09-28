// Encode the approved transparent artwork at native application/extension sizes.
// No recoloring or shape editing occurs here. Install sharp locally or set PINGYI_SHARP_MODULE.
const sharp = require(process.env.PINGYI_SHARP_MODULE || 'sharp');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const source = path.join(root, 'design-assets/screen-insight-icon-source.png');
const assets = path.join(root, 'src/PingYi.App/Assets');
const extension = path.join(root, 'browser-extension');
(async () => {
  await sharp(source).resize(512, 512).png().toFile(path.join(assets, 'screen-insight-icon-512.png'));
  await sharp(source).resize(256, 256).png().toFile(path.join(assets, 'screen-insight-icon.png'));
  const sizes = [16, 24, 32, 48, 64, 128, 256];
  const images = await Promise.all(sizes.map(size => sharp(source).resize(size, size).png().toBuffer()));
  const header = Buffer.alloc(6 + sizes.length * 16);
  header.writeUInt16LE(1, 2); header.writeUInt16LE(sizes.length, 4);
  let offset = header.length;
  images.forEach((image, index) => {
    const pos = 6 + index * 16, size = sizes[index];
    header[pos] = size === 256 ? 0 : size; header[pos + 1] = header[pos];
    header.writeUInt16LE(1, pos + 4); header.writeUInt16LE(32, pos + 6);
    header.writeUInt32LE(image.length, pos + 8); header.writeUInt32LE(offset, pos + 12); offset += image.length;
  });
  fs.writeFileSync(path.join(assets, 'screen-insight-icon.ico'), Buffer.concat([header, ...images]));
  for (const size of [16, 32, 48, 128]) {
    await sharp(source).resize(size, size).png().toFile(path.join(extension, `icon-${size}.png`));
  }
  await sharp(source).resize(128, 128).png().toFile(path.join(extension, 'icon.png'));
  console.log('Encoded approved logo: desktop PNG/ICO and extension 16/32/48/128 PNG.');
})().catch(error => { console.error(error.message); process.exitCode = 1; });
