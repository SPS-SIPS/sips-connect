import axios from "axios";

export function extractErrorMessage(error: unknown): string {
    if (axios.isAxiosError(error)) {
        const message =
            error.response?.data?.message ||
            error.response?.statusText ||
            error.message;
        return message || "An unknown Axios error occurred.";
    }

    if (error instanceof Error) {
        return error.message;
    }

    return "An unexpected error occurred.";
}
