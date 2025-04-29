import React, {useState} from 'react';
import {useRouter} from 'next/router';
import Link from 'next/link';
import {
    FiHome,
    FiChevronRight,
    FiChevronDown,
    FiX, FiCreditCard, FiEdit
} from 'react-icons/fi';

import styles from './Sidebar.module.css'
import {FaCogs, FaEye, FaFileAlt, FaPlug, FaPlusCircle, FaQrcode, FaRocket, FaTools} from "react-icons/fa";

interface NavItem {
    title: string;
    path: string;
    icon: React.ReactNode;
    items?: NavItem[];
}

interface SideNavProps {
    isOpen: boolean;
    onClose: () => void;
    isMobile: boolean;
}

const Sidebar: React.FC<SideNavProps> = ({isOpen, onClose, isMobile}) => {
    const router = useRouter();
    const [expandedItems, setExpandedItems] = useState<Record<string, boolean>>({});

    const navItems: NavItem[] = [
        {
            title: 'Dashboard',
            path: '/',
            icon: <FiHome className={styles.icon}/>,
        },
        {
            title: 'Documentation',
            path: '/docs',
            icon: <FaFileAlt className={styles.icon}/>,

            items: [
                {title: 'Getting Started', path: '/docs/getting-started', icon: <FiChevronRight className={styles.icon}/>},
                {title: 'API Reference', path: '/docs/api-reference', icon: <FiChevronRight className={styles.icon}/>},
            ],
        },
        {
            title: 'Configurations',
            path: '/config',
            icon: <FaCogs className={styles.icon}/>,
            items: [
                {
                    title: 'API Adapters',
                    path: '/config/endpoint',
                    icon: <FiChevronRight className={styles.icon}/>
                },
                {
                    title: 'Core',
                    path: '/config/core',
                    icon: <FiChevronRight className={styles.icon} />,
                },
                {
                    title: 'EMV',
                    path: '/config/emv',
                    icon: <FiChevronRight className={styles.icon} />,
                },
                {
                    title: 'Hosts',
                    path: '/config/hosts',
                    icon: <FiChevronRight className={styles.icon} />,
                },
                {
                    title: 'ISO 20022',
                    path: '/config/iso20022',
                    icon: <FiChevronRight className={styles.icon} />,
                },
                {
                    title: 'Origins',
                    path: '/config/origins',
                    icon: <FiChevronRight className={styles.icon} />,
                },
                {
                    title: 'XAdES',
                    path: '/config/xades',
                    icon: <FiChevronRight className={styles.icon} />,
                },
            ],
        },
        {
            title: 'Transactions',
            path: '/transactions',
            icon: <FiCreditCard className={styles.icon} />,
            items: [
                {
                    title: 'Transaction List',
                    path: '/transactions/list',
                    icon: <FiChevronRight className={styles.icon} />,
                },
                {
                    title: 'ISO Messages',
                    path: '/transactions/iso-messages',
                    icon: <FiChevronRight className={styles.icon} />,
                },
            ],
        },
        {
            title: 'API Tester',
            path: '/api-tester',
            icon: <FaRocket className={styles.icon}/>,
            items: [
                {
                    title: 'Transaction Endpoints',
                    path: '/api-testing/transactions',
                    icon: <FiCreditCard   className={styles.icon}/>,
                    items: [
                        {title: 'Verify API', path: '/api-tester/verify', icon: <FiChevronRight className={styles.icon}/>},
                        {title: 'Payment API', path: '/api-tester/payment', icon: <FiChevronRight className={styles.icon}/>},
                        {title: 'Status API', path: '/api-tester/status', icon: <FiChevronRight className={styles.icon}/>},
                        {title: 'Return API', path: '/api-tester/return', icon: <FiChevronRight className={styles.icon}/>},
                    ],
                },
                {
                    title: 'QR Code',
                    path: '/api-tester/qr-codes',
                    icon: <FaQrcode className={styles.icon}/>,
                    items: [
                        {
                            title: 'Generate QR Codes',
                            path: '/api-tester/qr-codes/generate',
                            icon: <FaPlusCircle className={styles.icon}/>,
                            items: [
                                {
                                    title: 'Merchant QR',
                                    path: '/api-tester/qr-codes/generate/merchant',
                                    icon: <FiChevronRight className={styles.icon}/>
                                },
                                {
                                    title: 'Person QR',
                                    path: '/api-tester/qr-codes/generate/personal',
                                    icon: <FiChevronRight className={styles.icon}/>
                                }
                            ],
                        },
                        {
                            title: 'Parse QR Codes',
                            path: '/api-tester/qr-codes/parse',
                            icon: <FaEye className={styles.icon}/>,
                            items: [
                                {
                                    title: 'Merchant QR',
                                    path: '/api-tester/qr-codes/parse/merchant',
                                    icon: <FiChevronRight className={styles.icon}/>
                                },
                                {
                                    title: 'Person QR',
                                    path: '/api-tester/qr-codes/parse/personal',
                                    icon: <FiChevronRight className={styles.icon}/>
                                }
                            ],
                        }
                    ],
                },
            ],
        },
        {
            title: 'Request Builder',
            path: '/request-builder',
            icon: <FaTools className={styles.icon}/>,
        },
    ];

    const toggleExpand = (title: string) => {
        setExpandedItems(prev => ({...prev, [title]: !prev[title]}));
    };

    const isActive = (path: string) => {
        return router.pathname === path || router.pathname.startsWith(`${path}/`);
    };

    const handleItemClick = (path: string) => {
        router.push(path);
        if (isMobile) onClose();
    };

    const renderNavItem = (item: NavItem, depth = 0) => {
        const hasChildren = item.items && item.items.length > 0;
        const isExpanded = expandedItems[item.title];

        return (
            <div key={item.path} className={styles.navItemContainer}>
                <div
                    className={`${styles.navItem} ${isActive(item.path) ? styles.active : ''}`}
                    style={{paddingLeft: `${1 + depth}rem`}}
                    onClick={() => hasChildren ? toggleExpand(item.title) : handleItemClick(item.path)}
                >
                    <span className={styles.iconWrapper}>{item.icon}</span>
                    <span className={styles.navText}>{item.title}</span>
                    {hasChildren && (
                        <span className={styles.chevron}>
              {isExpanded ? <FiChevronDown/> : <FiChevronRight/>}
            </span>
                    )}
                </div>

                {hasChildren && isExpanded && (
                    <div className={styles.subItems}>
                        {item.items?.map(subItem => renderNavItem(subItem, depth + 1))}
                    </div>
                )}
            </div>
        );
    };

    return (
        <nav className={`${styles.sideNav} ${isOpen ? styles.open : ''}`}>
            {/* Mobile header with close button */}
            {isMobile && (
                <div className={styles.mobileHeader}>
                    <div className={styles.logoContainer}>
                        <Link href="/" passHref>
                            <span className={styles.logo}>SPS</span>
                        </Link>
                    </div>
                    <button
                        className={styles.closeButton}
                        onClick={onClose}
                        aria-label="Close menu"
                    >
                        <FiX size={24}/>
                    </button>
                </div>
            )}
            <div className={styles.navItems}>
                {navItems.map(item => renderNavItem(item))}
            </div>
        </nav>
    );
};

export default Sidebar;