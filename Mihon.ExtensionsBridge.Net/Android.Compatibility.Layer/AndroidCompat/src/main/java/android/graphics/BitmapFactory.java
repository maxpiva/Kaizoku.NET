package android.graphics;

import java.awt.image.BufferedImage;
import java.io.ByteArrayInputStream;
import java.io.InputStream;
import java.io.IOException;
import java.util.Iterator;
import javax.imageio.ImageIO;
import javax.imageio.ImageReader;
import javax.imageio.stream.ImageInputStream;

public class BitmapFactory {
    public static Bitmap decodeStream(InputStream inputStream) {
        Bitmap bitmap = null;

        try {
            ImageInputStream imageInputStream = ImageIO.createImageInputStream(inputStream);
            Iterator<ImageReader> imageReaders = ImageIO.getImageReaders(imageInputStream);

            if (!imageReaders.hasNext()) {
                throw new IllegalArgumentException("no reader for image");
            }

            ImageReader imageReader = imageReaders.next();
            imageReader.setInput(imageInputStream);

            BufferedImage image = imageReader.read(0, imageReader.getDefaultReadParam());
            bitmap = new Bitmap(image);

            imageReader.dispose();
        } catch (IOException ex) {
            throw new RuntimeException(ex);
        }

        return bitmap;
    }

    public static Bitmap decodeByteArray(byte[] data, int offset, int length) {
        return decodeByteArray(data, offset, length, null);
    }

    public static Bitmap decodeByteArray(byte[] data, int offset, int length, Options opts) {
        if (data == null) {
            throw new NullPointerException("data");
        }
        if (offset < 0 || length < 0 || offset > data.length) {
            throw new ArrayIndexOutOfBoundsException("Invalid offset/length");
        }

        int availableLength = Math.min(length, data.length - offset);
        Bitmap bitmap = null;

        ByteArrayInputStream byteArrayStream = new ByteArrayInputStream(data, offset, availableLength);
        try {
            BufferedImage image = ImageIO.read(byteArrayStream);
            if (image != null) {
                if (opts != null) {
                    opts.outWidth = image.getWidth();
                    opts.outHeight = image.getHeight();
                }
                if (opts == null || !opts.inJustDecodeBounds) {
                    bitmap = new Bitmap(image);
                }
            } else if (opts != null) {
                opts.outWidth = 0;
                opts.outHeight = 0;
            }
        } catch (IOException ex) {
            throw new RuntimeException(ex);
        }

        return bitmap;
    }

    public static final class Options {
        public boolean inJustDecodeBounds;
        public Bitmap.Config inPreferredConfig = Bitmap.Config.ARGB_8888;
        public int inSampleSize = 1;
        public int outWidth;
        public int outHeight;
    }
}
