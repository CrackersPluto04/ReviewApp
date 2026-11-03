package com.reviewapp.android.data.local

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Matrix
import android.net.Uri
import androidx.exifinterface.media.ExifInterface
import com.reviewapp.android.util.ImageDownscale
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.io.IOException

/**
 * The photos of camera recognition, only ever in the app's cache: the camera's original and the prepared upload
 * (upright, at most 1280 px, JPEG). Nothing goes to the gallery, and the files are deleted once they're not needed.
 */
class RecognitionPhotos(context: Context) {
    private val resolver = context.contentResolver
    private val directory = File(context.cacheDir, "recognition")

    /** Where the camera writes a new photo. */
    fun newCaptureFile(): File = newFile("capture")

    /** Turns a photo (camera file or a picked image) into the upload. Off the main thread; null if it can't be decoded. */
    suspend fun prepare(source: Uri): File? = withContext(Dispatchers.IO) {
        try {
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            resolver.openInputStream(source)?.use { BitmapFactory.decodeStream(it, null, bounds) }
            if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return@withContext null

            val options = BitmapFactory.Options().apply { inSampleSize = ImageDownscale.sampleSize(bounds.outWidth, bounds.outHeight) }
            val decoded = resolver.openInputStream(source)?.use { BitmapFactory.decodeStream(it, null, options) } ?: return@withContext null

            val orientation = resolver.openInputStream(source)?.use {
                ExifInterface(it).getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL)
            } ?: ExifInterface.ORIENTATION_NORMAL

            val upright = scaleAndRotate(decoded, ImageDownscale.rotationDegrees(orientation))
            val file = newFile("upload")
            file.outputStream().use { upright.compress(Bitmap.CompressFormat.JPEG, ImageDownscale.JPEG_QUALITY, it) }
            upright.recycle()
            file
        } catch (_: IOException) {
            null
        } catch (_: SecurityException) {
            null
        } catch (_: OutOfMemoryError) {
            null
        }
    }

    fun delete(path: String?) {
        if (path != null) File(path).takeIf { it.parentFile == directory }?.delete()
    }

    /** Leftovers of an earlier visit (e.g. the app was killed while a photo was on screen). */
    fun clear() {
        directory.listFiles()?.forEach { it.delete() }
    }

    private fun scaleAndRotate(bitmap: Bitmap, degrees: Int): Bitmap {
        val target = ImageDownscale.fitWithin(bitmap.width, bitmap.height)
        val matrix = Matrix().apply {
            postScale(target.width.toFloat() / bitmap.width, target.height.toFloat() / bitmap.height)
            if (degrees != 0) postRotate(degrees.toFloat())
        }
        val result = Bitmap.createBitmap(bitmap, 0, 0, bitmap.width, bitmap.height, matrix, true)
        if (result !== bitmap) bitmap.recycle()
        return result
    }

    private fun newFile(prefix: String): File {
        directory.mkdirs()
        return File(directory, "$prefix-${System.currentTimeMillis()}.jpg")
    }
}
