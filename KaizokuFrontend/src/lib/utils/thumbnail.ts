import { getApiConfig } from "@/lib/api/config";

export const formatThumbnailUrl = (thumbnailUrl?: string): string => {
  const config = getApiConfig();
  if (!thumbnailUrl) {
    return `${config.baseUrl}/image/unknown`;
  }
  if (thumbnailUrl.startsWith("http")) {
    return thumbnailUrl;
  }
  return `${config.baseUrl}${thumbnailUrl}`;
};
