import React from 'react';
import styles from './Footer.module.css';
import Image from "next/image";
import Link from "next/link";
import {FacebookIcon, GitHubIcon, TwitterIcon, YouTubeIcon} from "../../../public/icons/SocialIcons";

const Footer = () => {
    const currentYear = new Date().getFullYear();

    return (
        <footer className={styles.footer}>
            <div className={styles.footerContent}>
                <div className={styles.brandSection}>
                    <div className={styles.logo}>
                        <Link href="/" passHref aria-label="Go to homepage">
                            <Image src={"/images/logo-sps-01.svg"} alt="Somali Payment Switch Logo" width={200} height={40} />
                        </Link>
                    </div>
                    <p className={styles.tagline}>Powering Somalia&apos;s Digital Payments</p>
                    <div className={styles.socialLinks}>
                        <a href="https://www.facebook.com/SPSwitch247/" aria-label="Facebook" target="_blank" rel="noopener noreferrer">
                            <FacebookIcon />
                        </a>
                        <a href="https://x.com/SPSwitch247" aria-label="Twitter" target="_blank" rel="noopener noreferrer">
                            <TwitterIcon />
                        </a>
                        <a href="https://www.youtube.com/@centralbankofsomalia9572" aria-label="YouTube" target="_blank" rel="noopener noreferrer">
                            <YouTubeIcon />
                        </a>
                        <a href="https://github.com/SPS-SIPS" aria-label="GitHub" target="_blank" rel="noopener noreferrer">
                            <GitHubIcon />
                        </a>
                    </div>
                </div>

                <div className={styles.linkColumns}>
                    <div className={styles.linkColumn}>
                        <h4>Services</h4>
                        <ul>
                            <li><a href="https://github.com/SPS-SIPS/SIPS.Connect/blob/main/Readme.md" target="_blank">API Documentation</a></li>
                            <li><a href="#">Security</a></li>
                            <li><a href="#">APIs</a></li>
                        </ul>
                    </div>
                    <div className={styles.linkColumn}>
                        <h4>Company</h4>
                        <ul>
                            <li><a href="#">About</a></li>
                            <li><a href="#">Link</a></li>
                            <li><a href="#">Contact</a></li>
                        </ul>
                    </div>
                    <div className={styles.linkColumn}>
                        <h4>Legal</h4>
                        <ul>
                            <li><a href="#">Privacy</a></li>
                            <li><a href="#">Terms</a></li>
                            <li><a href="#">Compliance</a></li>
                        </ul>
                    </div>
                </div>
            </div>

            <div className={styles.footerBottom}>
                <p>© {currentYear} Somali Payment Switch. All rights reserved.</p>
            </div>
        </footer>
    );
};

export default Footer;