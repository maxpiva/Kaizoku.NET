import { toast as sonnerToast } from "sonner";

export interface Toast {
  id: string;
  title?: string;
  description?: string;
  variant?: "default" | "destructive" | "success";
  duration?: number;
}

interface ToastOptions {
  title?: string;
  description?: string;
  variant?: "default" | "destructive" | "success";
}

const useToast = () => {
  const toast = ({ title, description, variant }: ToastOptions) => {
    if (variant === "destructive") {
      sonnerToast.error(title ?? "Error", {
        description,
      });
    } else if (variant === "success") {
      sonnerToast.success(title ?? "Success", {
        description,
      });
    } else {
      sonnerToast(title ?? "Info", {
        description,
      });
    }
  };

  return {
    toast,
    dismiss: () => {
      sonnerToast.dismiss();
    },
  };
};

export { useToast };
