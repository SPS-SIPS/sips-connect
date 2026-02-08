import React, {useEffect, useRef, useState} from 'react';
import {FiChevronDown, FiLogOut, FiUser} from 'react-icons/fi';
import styles from '../Navbar.module.css';
import {useAuthentication} from "../../../../auth/AuthProvider";
import {useRouter} from "next/router";

interface AvatarDropdownProps {
    firstName: string;
}

export const AvatarDropdown: React.FC<AvatarDropdownProps> = ({firstName}) => {
    const [isDropdownOpen, setIsDropdownOpen] = useState(false);
    const dropdownRef = useRef<HTMLDivElement>(null);
    const router = useRouter();
    const {logout} = useAuthentication();

    useEffect(() => {
        const handleClickOutside = (event: MouseEvent) => {
            if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
                setIsDropdownOpen(false);
            }
        };

        document.addEventListener('mousedown', handleClickOutside);
        return () => document.removeEventListener('mousedown', handleClickOutside);
    }, []);

    const goToProfile = () => {
        router.push('/profile');
    };

    const getFirstLetter = (name: string) => name.charAt(0).toUpperCase();
    const getDisplayName = (name: string) => name.length > 10 ? `${name.substring(0, 8)}...` : name;

    return (
        <div className={styles.avatarDropdown} ref={dropdownRef}>
            <button
                className={styles.avatarButton}
                onClick={() => setIsDropdownOpen(!isDropdownOpen)}
                aria-expanded={isDropdownOpen}
                aria-label="User menu"
            >
                <div className={styles.avatar}>
                    {getFirstLetter(firstName)}
                </div>
                <span className={styles.userName} title={firstName}>
          {getDisplayName(firstName)}
        </span>
                <FiChevronDown className={`${styles.chevron} ${isDropdownOpen ? styles.rotated : ''}`}/>
            </button>

            {isDropdownOpen && (
                <div className={styles.dropdownMenu}>
                    <button className={styles.dropdownItem} onClick={goToProfile}>
                        <FiUser className={styles.dropdownIcon}/>
                        <span>Profile</span>
                    </button>
                    <button className={styles.dropdownItem} onClick={logout}>
                        <FiLogOut className={styles.dropdownIcon}/>
                        <span>Logout</span>
                    </button>
                </div>
            )}
        </div>
    );
};