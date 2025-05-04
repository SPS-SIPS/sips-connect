export const baseURL = process.env.NEXT_PUBLIC_API_URL;
const apiKey = process.env.NEXT_PUBLIC_API_Key;
const apiSecret = process.env.NEXT_PUBLIC_API_Secret;

export const apiAuth = `${apiKey}:${apiSecret}`;
