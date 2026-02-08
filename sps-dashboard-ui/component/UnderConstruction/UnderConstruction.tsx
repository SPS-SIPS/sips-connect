import Head from "next/head";
import styles from "./UnderConstruction.module.css";
import { useState, useEffect } from "react";
import Link from "next/link";

export default function UnderConstruction() {
    const [progress, setProgress] = useState(0);

    useEffect(() => {
        const timer = setInterval(() => {
            setProgress(prev => {
                if (prev >= 90) {
                    return prev;
                }
                if (prev >= 85) {
                    return prev + 0.5;
                }
                return prev < 100 ? prev + 1 : prev;
            });
        }, 50);

        return () => clearInterval(timer);
    }, []);

    return (
        <>
            <Head>
                <title>Under Construction</title>
                <link rel="icon" href="/icons/icon.png"/>
                <meta name="description" content="Exciting things are coming soon! Stay tuned for our website launch." />
            </Head>

            <div className={styles.container}>
                <div className={styles.content}>
                    <div className={styles.constructionWrapper}>
                        <div className={styles.tools}>
                            <div className={styles.hammer}></div>
                            <div className={styles.gear}></div>
                        </div>
                        <div className={styles.blueprint}></div>
                    </div>

                    <h1 className={styles.title}>
                        Page in Progress!
                        <span className={styles.highlight}>Coming Soon</span>
                    </h1>

                    <p className={styles.message}>
                        We&apos;re currently building this page with care.
                        Check back soon to see the final result!
                    </p>

                    <div className={styles.progressWrapper}>
                        <div className={styles.progressHeader}>
                            <span>Construction Progress</span>
                            <span>{Math.min(100, Math.floor(progress))}%</span>
                        </div>
                        <div className={styles.progressBar}>
                            <div
                                className={styles.progressFill}
                                style={{ width: `${progress}%` }}
                            >
                                <div className={styles.progressSparkle}></div>
                            </div>
                        </div>
                    </div>

                    <div className={styles.ctaGroup}>
                        <Link href="/" className={styles.homeLink}>
                            ← Return to Homepage
                        </Link>
                        <div className={styles.contactLink}>
                            Need immediate access? <a href="mailto:info@sps.so">Contact us</a>
                        </div>
                    </div>
                </div>
            </div>
        </>
    );
}