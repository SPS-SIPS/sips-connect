/**
 * Validates a URL string with configurable options
 * @param url The URL string to validate
 * @param options Validation configuration options
 * @returns Boolean indicating if the URL is valid according to the options
 */
export const validateUrl = (
    url: string,
    options: {
        allowedProtocols?: string[];
        requirePath?: boolean;
        allowLocalhost?: boolean;
    } = {
        allowedProtocols: ['http:', 'https:'],
        requirePath: false,
        allowLocalhost: true
    }
): boolean => {
    try {
        const { protocol, hostname, pathname } = new URL(url);

        // Validate protocol
        const isValidProtocol = options.allowedProtocols?.includes(protocol) ?? true;

        // Validate hostname
        const isValidHostname = hostname !== '' && (
            options.allowLocalhost ? true : hostname !== 'localhost'
        );

        // Validate path if required
        const isValidPath = options.requirePath ? pathname !== '/' : true;

        return isValidProtocol && isValidHostname && isValidPath;
        // eslint-disable-next-line @typescript-eslint/no-unused-vars
    } catch (error) {
        return false;
    }
};