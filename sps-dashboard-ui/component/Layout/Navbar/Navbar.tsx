import React from 'react';
import styles from './Navbar.module.css';
import Image from 'next/image';
import { AvatarDropdown } from "./AvatarDropdown/AvatarDropdown";
import Link from "next/link";
import { FiMenu } from 'react-icons/fi';
import {useAuthentication} from "../../../auth/AuthProvider";

interface NavbarProps {
    onMenuToggle: () => void;
    isMobile: boolean;
}

export const Navbar: React.FC<NavbarProps> = ({onMenuToggle, isMobile }) => {
    const { userName } = useAuthentication();
    return (
        <header className={styles.navbar}>
            <div className={styles.leftSection}>
                {/* Mobile Burger Menu - Only shows on mobile */}
                {isMobile && (
                    <button
                        className={styles.menuButton}
                        onClick={onMenuToggle}
                        aria-label="Toggle menu"
                    >
                        <FiMenu size={24} />
                    </button>
                )}

                <Link href="/" passHref>
                    <div className={styles.logoContainer}>
                        <Image
                            src="/images/logo-sps-01.svg"
                            alt="SPS Logo"
                            width={120}
                            height={40}
                            className={styles.logo}
                            priority
                        />
                    </div>
                </Link>
            </div>

            <div className={styles.rightSection}>
                <AvatarDropdown firstName={userName!} />
            </div>
        </header>
    );
};