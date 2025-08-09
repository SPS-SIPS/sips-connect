// Load environment variables from .env.local
import dotenv from 'dotenv';
dotenv.config({ path: '.env.local' });
// Custom HTTPS server for Next.js (ESM)
import { createServer } from 'https';
import next from 'next';
import { readFileSync } from 'fs';

const dev = process.env.NODE_ENV !== 'production';
const app = next({ dev });
const handle = app.getRequestHandler();

const certPath = process.env.NEXTJS_TLS_CERT || './certs/tls.crt';
const keyPath = process.env.NEXTJS_TLS_KEY || './certs/tls.key';
const httpsOptions = {
    key: readFileSync(keyPath),
    cert: readFileSync(certPath),
};

app.prepare().then(() => {
    createServer(httpsOptions, (req, res) => {
        // Pass req.url as-is to Next.js handler to avoid redirect issues
        handle(req, res);
    }).listen(3000, err => {
        if (err) throw err;
        console.log('> Server started on https://localhost:3000');
    });
});
