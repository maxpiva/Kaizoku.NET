import { SeriesStatus } from "@/lib/api/types";

export interface StatusDisplay {
  text: string;
  color: string;
}

/**
 * Get display text and color for a series status
 */
export const getStatusDisplay = (status: SeriesStatus): StatusDisplay => {
  switch (status) {
    case SeriesStatus.ONGOING:
      return { text: "Ongoing", color: "bg-green-500" };
    case SeriesStatus.COMPLETED:
      return { text: "Completed", color: "bg-blue-500" };
    case SeriesStatus.LICENSED:
      return { text: "Licensed", color: "bg-purple-500" };
    case SeriesStatus.PUBLISHING_FINISHED:
      return { text: "Publishing Finished", color: "bg-blue-600" };
    case SeriesStatus.CANCELLED:
      return { text: "Cancelled", color: "bg-red-500" };
    case SeriesStatus.ON_HIATUS:
      return { text: "On Hiatus", color: "bg-yellow-500" };
    case SeriesStatus.DISABLED:
      return { text: "Disabled", color: "bg-gray-600" };
    default:
      return { text: "Unknown", color: "bg-gray-500" };
  }
};
