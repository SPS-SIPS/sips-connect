import React, {useEffect, useState} from "react";
import styles from "../styles/Profile.module.css";
import {useAuthentication} from "../auth/AuthProvider";

const Profile: React.FC = () => {
    const {userName, roles, keycloak, authToken} = useAuthentication();
    const [userInfo, setUserInfo] = useState<Record<string, any>>({});
    const [showToken, setShowToken] = useState(false);
    const [copied, setCopied] = useState(false);
    const [copyError, setCopyError] = useState<string | null>(null);

    useEffect(() => {
        const fetchUserInfo = async () => {
            if (keycloak?.authenticated) {
                try {
                    const info = await keycloak.loadUserInfo();
                    console.log(info);
                    setUserInfo(info);
                } catch (error) {
                    console.error("Failed to load user info", error);
                }
            }
        };
        fetchUserInfo();
    }, [keycloak]);

    const getInitials = () => {
        const firstName = userInfo?.given_name || '';
        const lastName = userInfo?.family_name || '';

        if (firstName || lastName) {
            return `${firstName[0] || ''}${lastName[0] || ''}`.toUpperCase();
        }
        return userName?.substring(0, 2).toUpperCase() || '';
    };

    const formatDate = (timestamp?: number) => {
        if (!timestamp) return 'N/A';
        return new Date(timestamp * 1000).toLocaleDateString();
    };

    const handleCopyToken = async () => {
        if (!authToken) return;

        try {
            await navigator.clipboard.writeText(authToken);
            setCopied(true);
            setCopyError(null);
            setTimeout(() => setCopied(false), 2000);
            // eslint-disable-next-line @typescript-eslint/no-unused-vars
        } catch (err) {
            setCopyError('Failed to copy token');
            setTimeout(() => setCopyError(null), 3000);
        }
    };
    return (<div className={styles.profileContainer}>
        <div className={styles.backgroundBox}>
            <div className={styles.initialsCircle}>
                <span className={styles.initialsText}>{getInitials()}</span>
            </div>
        </div>

        <div className={styles.detailsContainer}>
            <h2 className={styles.fullName}>
                {[userInfo?.given_name, userInfo?.family_name].filter(Boolean).join(' ') || userName}
            </h2>

            <div className={styles.detailSection}>
                <h3 className={styles.sectionTitle}>Personal Information</h3>
                <div className={styles.detailItem}>
                    <span className={styles.detailLabel}>Email:</span>
                    <span>{userInfo?.email || 'N/A'}</span>
                </div>
                <div className={styles.detailItem}>
                    <span className={styles.detailLabel}>Phone:</span>
                    <span>{userInfo?.attributes?.phoneNumber?.[0] || 'N/A'}</span>
                </div>
                <div className={styles.detailItem}>
                    <span className={styles.detailLabel}>Birthdate:</span>
                    <span>{userInfo?.birthdate || 'N/A'}</span>
                </div>
            </div>

            <div className={styles.detailSection}>
                <h3 className={styles.sectionTitle}>Account Details</h3>
                <div className={styles.detailItem}>
                    <span className={styles.detailLabel}>Username:</span>
                    <span>{userInfo?.preferred_username || 'N/A'}</span>
                </div>
                <div className={styles.detailItem}>
                    <span className={styles.detailLabel}>Account Created:</span>
                    <span>{formatDate(userInfo?.createdTimestamp)}</span>
                </div>
                <div className={styles.detailItem}>
                    <span className={styles.detailLabel}>Last Login:</span>
                    <span>{formatDate(userInfo?.lastLoginTimestamp)}</span>
                </div>
            </div>

            <div className={styles.detailSection}>
                <h3 className={styles.sectionTitle}>Security</h3>
                <div className={styles.detailItem}>
                    <span className={styles.detailLabel}>Roles:</span>
                    <span>{roles.join(", ") || 'N/A'}</span>
                </div>
                <div className={styles.detailItem}>
                    <span className={styles.detailLabel}>Email Verified:</span>
                    <span>{userInfo?.email_verified ? 'Yes' : 'No'}</span>
                </div>
                <div className={styles.detailItem}>
                    <span className={styles.detailLabel}>Access Token:</span>
                    <div className={styles.tokenContainer}>
                        <div className={styles.tokenActions}>
                            <button
                                onClick={() => setShowToken(!showToken)}
                                className={styles.tokenToggle}
                            >
                                {showToken ? 'Hide' : 'Show'} Token
                            </button>
                            <button
                                onClick={handleCopyToken}
                                className={styles.copyButton}
                                disabled={!authToken}
                            >
                                {copied ? 'Copied!' : 'Copy'}
                            </button>
                        </div>
                        {copyError && (<div className={styles.copyError}>{copyError}</div>)}
                        {showToken && (<pre className={styles.tokenPreview}>
                        {authToken || 'No token available'}
                        </pre>)}
                    </div>
                </div>
            </div>

            {userInfo?.attributes?.jobTitle && (<div className={styles.detailSection}>
                <h3 className={styles.sectionTitle}>Employment</h3>
                <div className={styles.detailItem}>
                    <span className={styles.detailLabel}>Job Title:</span>
                    <span>{userInfo.attributes.jobTitle[0]}</span>
                </div>
                <div className={styles.detailItem}>
                    <span className={styles.detailLabel}>Department:</span>
                    <span>{userInfo.attributes.department?.[0] || 'N/A'}</span>
                </div>
            </div>)}
        </div>
    </div>);
};

export default Profile;