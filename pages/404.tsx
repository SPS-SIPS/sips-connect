import Head from "next/head";
import Link from "next/link";
import styles from "../styles/NotFound.module.css";
import React from "react";
import ProtectedRoute from "../component/common/ProtectedRoute";

export default function NotFoundPage() {
    return (
        <ProtectedRoute>
            <Head>
                <title>404 - Page Not Found</title>
                <meta name="description" content="The page you're looking for doesn't exist. Return to the homepage." />
            </Head>
            <div className={styles.container}>
                <div className={styles.content}>
                    <h1 className={styles.errorCode}>404</h1>
                    <h2 className={styles.title}>Page Not Found</h2>
                    <p className={styles.message}>
                        Oops! The page you&apos;re looking for doesn&apos;t exist or has been moved.
                    </p>
                    <Link href="/" className={styles.homeButton}>
                        Return to Homepage
                    </Link>
                </div>
            </div>
        </ProtectedRoute>
    );
}