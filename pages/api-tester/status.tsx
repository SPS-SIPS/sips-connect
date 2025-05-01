import ApiRequestTester from "../../component/ApiRequestTester/ApiRequestTester";
import RoleGuard from "../../auth/RoleGuard";

const Status= () => {

    return (
        <RoleGuard allowedRoles={['gateway']}>
            <ApiRequestTester
                title={"API Status Request Tester "}
                placeholder={"https://example.com/api/v1/Gateway/Status"}
                selectedRequest={"StatusRequest"}
            />
        </RoleGuard>
    );
};

export default Status;