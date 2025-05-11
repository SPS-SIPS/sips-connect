import React, {useState} from 'react';
import {useRouter} from 'next/router';
import Link from 'next/link';
import {
    FiHome,
    FiChevronRight,
    FiChevronDown,
    FiX, FiCreditCard,
} from 'react-icons/fi';

import styles from './Sidebar.module.css'
import {FaCogs, FaEye, FaFileAlt, FaPlusCircle, FaQrcode, FaRocket, FaTools} from "react-icons/fa";
import {useAuthentication} from "../../../auth/AuthProvider";
import {AiOutlineDoubleLeft, AiOutlineDoubleRight} from "react-icons/ai";

interface NavItem {
    title: string;
    path: string;
    icon: React.ReactNode;
    roles?: string[];
    items?: NavItem[];
}

interface SideNavProps {
    isOpen: boolean;
    onClose: () => void;
    isMobile: boolean;
}

const Sidebar: React.FC<SideNavProps> = ({isOpen, onClose, isMobile}) => {
    const router = useRouter();
    const { roles } = useAuthentication();
    const [expandedItems, setExpandedItems] = useState<Record<string, boolean>>({});

    const navItems: NavItem[] = [
        {
            title: 'Dashboard',
            path: '/',
            icon: <FiHome className={styles.icon}/>,
            roles: ['transactions', 'iso_messages', 'configuration','som_qr','offline_access','gateway']
        },
        {
            title: 'Documentation',
            path: '/docs',
            icon: <FaFileAlt className={styles.icon}/>,
            roles: ['transactions', 'iso_messages', 'configuration','som_qr','offline_access','gateway'],
            items: [
                {
                    title: 'Getting Started',
                    path: '/docs/getting-started',
                    icon: <FiChevronRight className={styles.icon}/>,
                    roles: ['transactions', 'iso_messages', 'configuration','som_qr','offline_access','gateway']
                },
                {title: 'API Reference',
                    path: '/docs/api-reference',
                    icon: <FiChevronRight
                        className={styles.icon}/>,
                    roles: ['transactions', 'iso_messages', 'configuration','som_qr','offline_access','gateway']
                },
            ],
        },
        {
            title: 'Configurations',
            path: '/config',
            icon: <FaCogs className={styles.icon}/>,
            roles: ['configuration'],
            items: [
                {
                    title: 'API Adapters',
                    path: '/config/endpoint',
                    icon: <FiChevronRight className={styles.icon}/>
                },
                {
                    title: 'API Keys',
                    path: '/config/apiKeys',
                    icon: <FiChevronRight className={styles.icon} />,
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
            roles: ['transactions','iso_messages'],
            items: [
                {
                    title: 'Transaction List',
                    path: '/transactions/list',
                    icon: <FiChevronRight className={styles.icon} />,
                    roles: ['transactions'],
                },
                {
                    title: 'ISO Messages',
                    path: '/transactions/iso-messages',
                    icon: <FiChevronRight className={styles.icon} />,
                    roles: ['iso_messages'],
                },
            ],
        },
        {
            title: 'API Tester',
            path: '/api-tester',
            icon: <FaRocket className={styles.icon}/>,
            roles: ['som_qr','gateway'],
            items: [
                {
                    title: 'Transaction Endpoints',
                    path: '/api-testing/transactions',
                    icon: <FiCreditCard   className={styles.icon}/>,
                    roles: ['gateway'],
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
                    roles: ['som_qr'],
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
            roles: ['gateway'],
        },
    ];

    const toggleExpand = (title: string) => {
        setExpandedItems((prev) => ({ ...prev, [title]: !prev[title] }));
    };

    const isActive = (path: string) => {
        return router.pathname === path || router.pathname.startsWith(`${path}/`);
    };

    const handleItemClick = (path: string) => {
        router.replace(path);
        if (isMobile) onClose();
    };

    const renderNavItem = (item: NavItem, depth = 0) => {
        const hasChildren = item.items && item.items.length > 0;
        const isExpanded = expandedItems[item.title];

        if (item.roles && !item.roles.some(role => roles.includes(role))) {
            return null;
        }
        return (
            <div key={item.path} className={styles.navItemContainer}>
                <div
                    className={`${styles.navItem} ${isActive(item.path) ? styles.active : ''}`}
                    style={{ paddingLeft: `${1 + depth}rem` }}
                    onClick={() => hasChildren ? toggleExpand(item.title) : handleItemClick(item.path)}
                >
                    <span className={styles.iconWrapper}>{item.icon}</span>
                    <span className={styles.navText}>{item.title}</span>
                    {hasChildren && (
                        <span className={styles.chevron}>
              {isExpanded ? <FiChevronDown /> : <FiChevronRight />}
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

    function handleToggle() {
        onClose();
    }

    return (
        <nav className={styles.navigationContainer}>
            <div className={`${styles.sideNav} ${isOpen ? styles.open : styles.closed}`}>
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
                            <FiX size={24} />
                        </button>
                    </div>
                )}
                <div className={styles.navItems}>
                    {navItems.map(item => renderNavItem(item))}
                </div>
                {/* For non-mobile devices the toggleButton is rendered within the sidebar */}
                {!isMobile && isOpen && (
                    <div
                        className={`${styles.toggleButton} ${isOpen ? styles.openButton : ''}`}
                        aria-label="Toggle sidebar"
                        onClick={handleToggle}
                    >
                        <AiOutlineDoubleLeft />
                    </div>
                )}
            </div>
            {/* When sidebar is closed, the toggleButton is rendered separately */}
            {!isMobile && !isOpen && (
                <div
                    className={`${styles.toggleButton} ${!isOpen ? styles.closedButton : ''}`}
                    aria-label="Toggle sidebar"
                    onClick={handleToggle}
                >
                    <AiOutlineDoubleRight />
                </div>
            )}
        </nav>
    );
};

export default Sidebar;
