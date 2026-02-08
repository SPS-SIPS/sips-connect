import ApiRequestTester from "../../component/ApiRequestTester/ApiRequestTester";
import RoleGuard from "../../auth/RoleGuard";
import { baseURL } from "../../constants/constants";

const Status= () => {

    return (
        <RoleGuard allowedRoles={['gateway']}>
            <ApiRequestTester
                title={"API Status Request Tester "}
                placeholder={"https://example.com/api/v1/Gateway/Status"}
                selectedRequest={"StatusRequest"}
                initialUrl={`${baseURL}/api/v1/Gateway/Status`}
            />
        </RoleGuard>
    );
};

export default Status;