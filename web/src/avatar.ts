const MAX_INPUT_BYTES = 12 * 1024 * 1024

export async function avatarImageData(file: File): Promise<{ contentType: string; data: string }> {
  if (file.size > MAX_INPUT_BYTES) throw new Error('Choose an image smaller than 12 MB.')
  const bitmap = await createImageBitmap(file)
  try {
    const side = Math.min(bitmap.width, bitmap.height)
    const canvas = document.createElement('canvas')
    canvas.width = 320
    canvas.height = 320
    const context = canvas.getContext('2d')
    if (!context) throw new Error('Could not process this image.')
    context.drawImage(bitmap, (bitmap.width - side) / 2, (bitmap.height - side) / 2, side, side, 0, 0, 320, 320)
    const blob = await new Promise<Blob>((resolve, reject) =>
      canvas.toBlob((result) => result ? resolve(result) : reject(new Error('Could not process this image.')), 'image/jpeg', 0.86),
    )
    const bytes = new Uint8Array(await blob.arrayBuffer())
    let binary = ''
    for (let offset = 0; offset < bytes.length; offset += 0x8000) {
      binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000))
    }
    return { contentType: 'image/jpeg', data: btoa(binary) }
  } finally {
    bitmap.close()
  }
}
